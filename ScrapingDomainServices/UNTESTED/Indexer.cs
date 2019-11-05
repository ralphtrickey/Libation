﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ApplicationService;
using DataLayer;
using Dinah.Core;
using Dinah.Core.Collections.Generic;
using DTOs;
using FileManager;
using Newtonsoft.Json;

namespace ScrapingDomainServices
{
    public static class Indexer
    {
        #region library
        public static async Task<(int total, int newEntries)> IndexLibraryAsync(DirectoryInfo libDir)
        {
            var jsonFileInfos = WebpageStorage.GetJsonFiles(libDir);
            return await IndexLibraryAsync(jsonFileInfos);
        }

        public static async Task<(int total, int newEntries)> IndexLibraryAsync(List<FileInfo> jsonFileInfos)
        {
            var productItems = jsonFileInfos.SelectMany(fi => json2libraryDtos(fi)).ToList();
            var newEntries = await IndexLibraryAsync(productItems);
            return (productItems.Count, newEntries);
        }
        private static Regex jsonIsCollectionRegex = new Regex(@"^\s*\[\s*\{", RegexOptions.Compiled);
        private static IEnumerable<LibraryDTO> json2libraryDtos(FileInfo jsonFileInfo)
        {
            validateJsonFile(jsonFileInfo);

            var serialized = File.ReadAllText(jsonFileInfo.FullName);

            // collection
            if (jsonIsCollectionRegex.IsMatch(serialized))
                return JsonConvert.DeserializeObject<List<LibraryDTO>>(serialized);

            // single
            return new List<LibraryDTO> { JsonConvert.DeserializeObject<LibraryDTO>(serialized) };
        }

        // new full index or library-file import: re-create search index
        /// <returns>qty new entries</returns>
		public static async Task<int> IndexLibraryAsync(List<LibraryDTO> productItems)
        {
            if (productItems == null || !productItems.Any())
                return 0;

			productItems = filterAndValidate(productItems);

			using var context = LibationContext.Create();
            var dtoImporter = new DtoImporter(context);

            #region // benchmarks. re-importing a library with 500 books, all with book details json files
            /*
            dtoImporter.ReplaceLibrary           1.2 seconds
            SaveChanges()                        3.4
            ReloadBookDetails()                  1.3
            SaveChanges()                        1.4
            */
            #endregion
            // LONG RUNNING
            var newEntries = await Task.Run(() => dtoImporter.ReplaceLibrary(productItems));
            await context.SaveChangesAsync();

            // must be broken out. see notes in dtoImporter.ReplaceLibrary()
            // LONG RUNNING
            await Task.Run(() => dtoImporter.ReloadBookDetails(productItems));
            await context.SaveChangesAsync();

            await SearchEngineActions.FullReIndexAsync();

            return newEntries;
        }
        private static List<LibraryDTO> filterAndValidate(List<LibraryDTO> collection)
        {
            //debug//var episodes = collection.Where(dto => dto.IsEpisodes).ToList();

            // for now, do not add episodic content
            collection.RemoveAll(dto => dto.IsEpisodes);

            if (collection.Any(pi => string.IsNullOrWhiteSpace(pi.ProductId)))
                throw new Exception("All product items must contain a Product Id");

			return collection.DistinctBy(pi => pi.ProductId).ToList();

    //        var duplicateIds = collection
    //            .GroupBy(pi => pi.ProductId)
    //            .Where(grp => grp.Count() > 1)
    //            .Select(grp => grp.Key)
				//.ToList();

    //        if (duplicateIds.Any())
    //            throw new Exception("Cannot insert multiples of the same ProductId. Duplicates:"
    //                + duplicateIds
    //                .Select(a => "\r\n- " + a)
    //                .Aggregate((a, b) => a + b));
        }
        #endregion

        #region update book tags
        public static int IndexChangedTags(Book book)
        {
			// update disconnected entity
			using var context = LibationContext.Create();
            context.Update(book);
            var qtyChanges = context.SaveChanges();

            // this part is tags-specific
            if (qtyChanges > 0)
                SearchEngineActions.UpdateBookTags(book);

            return qtyChanges;
        }
        #endregion

        #region book details
        public static async Task IndexBookDetailsAsync(BookDetailDTO bookDetailDTO)
            => await indexBookDetailsAsync(bookDetailDTO, () => SearchEngineActions.ProductReIndexAsync(bookDetailDTO.ProductId));

        private static async Task indexBookDetailsAsync(BookDetailDTO bookDetailDTO, Func<Task> postIndexActionAsync)
        {
            if (bookDetailDTO == null)
                return;

            validate(bookDetailDTO);

			using var context = LibationContext.Create();
            var dtoImporter = new DtoImporter(context);
            // LONG RUNNING
            await Task.Run(() => dtoImporter.UpdateBookDetails(bookDetailDTO));
            context.SaveChanges();

            // after saving, delete orphan contributors
            var count = context.RemoveOrphans();
            if (count > 0) { } // don't think there's a to-do here

            await postIndexActionAsync?.Invoke();
        }
        private static void validate(BookDetailDTO bookDetailDTO)
        {
            if (string.IsNullOrWhiteSpace(bookDetailDTO.ProductId))
                throw new Exception("Product must contain a Product Id");
        }
        #endregion

        private static void validateJsonFile(FileInfo jsonFileInfo)
        {
            if (!jsonFileInfo.Extension.EqualsInsensitive(".json"))
                throw new Exception("Unsupported file types");
        }
    }
}
