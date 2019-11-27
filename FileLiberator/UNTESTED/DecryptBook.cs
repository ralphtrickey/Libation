﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AaxDecrypter;
using DataLayer;
using Dinah.Core;
using Dinah.Core.ErrorHandling;
using FileManager;

namespace FileLiberator
{
    /// <summary>
    /// Decrypt audiobook files
    /// 
    /// Processes:
    /// Download: download aax file: the DRM encrypted audiobook
    /// Decrypt: remove DRM encryption from audiobook. Store final book
    /// Backup: perform all steps (downloaded, decrypt) still needed to get final book
    /// </summary>
    public class DecryptBook : IDecryptable
    {
        public event EventHandler<string> Begin;
        public event EventHandler<string> StatusUpdate;
        public event EventHandler<string> DecryptBegin;

        public event EventHandler<string> TitleDiscovered;
        public event EventHandler<string> AuthorsDiscovered;
        public event EventHandler<string> NarratorsDiscovered;
        public event EventHandler<byte[]> CoverImageFilepathDiscovered;
        public event EventHandler<int> UpdateProgress;

        public event EventHandler<string> DecryptCompleted;
        public event EventHandler<string> Completed;

        public bool Validate(LibraryBook libraryBook)
            => AudibleFileStorage.AAX.Exists(libraryBook.Book.AudibleProductId)
            && !AudibleFileStorage.Audio.Exists(libraryBook.Book.AudibleProductId);

		// do NOT use ConfigureAwait(false) on ProcessAsync()
		// often calls events which prints to forms in the UI context
		public async Task<StatusHandler> ProcessAsync(LibraryBook libraryBook)
        {
            var displayMessage = $"[{libraryBook.Book.AudibleProductId}] {libraryBook.Book.Title}";

            Begin?.Invoke(this, displayMessage);

            try
            {
                var aaxFilename = AudibleFileStorage.AAX.GetPath(libraryBook.Book.AudibleProductId);

                if (aaxFilename == null)
                    return new StatusHandler { "aaxFilename parameter is null" };
                if (!FileUtility.FileExists(aaxFilename))
                    return new StatusHandler { $"Cannot find AAX file: {aaxFilename}" };
                if (AudibleFileStorage.Audio.Exists(libraryBook.Book.AudibleProductId))
                    return new StatusHandler { "Cannot find decrypt. Final audio file already exists" };

                var proposedOutputFile = Path.Combine(AudibleFileStorage.DecryptInProgress, $"[{libraryBook.Book.AudibleProductId}].m4b");
                var outputAudioFilename = await aaxToM4bConverterDecrypt(proposedOutputFile, aaxFilename);

                // decrypt failed
                if (outputAudioFilename == null)
                    return new StatusHandler { "Decrypt failed" };

                moveFilesToBooksDir(libraryBook.Book, outputAudioFilename);

                Dinah.Core.IO.FileExt.SafeDelete(aaxFilename);

                var statusHandler = new StatusHandler();
                var finalAudioExists = AudibleFileStorage.Audio.Exists(libraryBook.Book.AudibleProductId);
                if (!finalAudioExists)
                    statusHandler.AddError("Cannot find final audio file after decryption");
                return statusHandler;
            }
            finally
            {
                Completed?.Invoke(this, displayMessage);
            }
        }

        private async Task<string> aaxToM4bConverterDecrypt(string proposedOutputFile, string aaxFilename)
        {
            DecryptBegin?.Invoke(this, $"Begin decrypting {aaxFilename}");

            try
            {
                var converter = await AaxToM4bConverter.CreateAsync(aaxFilename, Configuration.Instance.DecryptKey);
                converter.AppName = "Libation";

                TitleDiscovered?.Invoke(this, converter.tags.title);
                AuthorsDiscovered?.Invoke(this, converter.tags.author);
                NarratorsDiscovered?.Invoke(this, converter.tags.narrator);
                CoverImageFilepathDiscovered?.Invoke(this, converter.coverBytes);

                converter.SetOutputFilename(proposedOutputFile);
                converter.DecryptProgressUpdate += (s, progress) => UpdateProgress?.Invoke(this, progress);

                // REAL WORK DONE HERE
                var success = await Task.Run(() => converter.Run());

                if (!success)
                {
                    Console.WriteLine("decrypt failed");
                    return null;
                }

                Configuration.Instance.DecryptKey = converter.decryptKey;

                return converter.outputFileName;
            }
            finally
            {
                DecryptCompleted?.Invoke(this, $"Completed decrypting {aaxFilename}");
            }
        }

        private static void moveFilesToBooksDir(Book product, string outputAudioFilename)
        {
            // files are: temp path\author\[asin].ext
            var m4bDir = new FileInfo(outputAudioFilename).Directory;
            var files = m4bDir
                .EnumerateFiles()
                .Where(f => f.Name.ContainsInsensitive(product.AudibleProductId))
                .ToList();

            // create final directory. move each file into it. MOVE AUDIO FILE LAST
            // new dir: safetitle_limit50char + " [" + productId + "]"

            // to prevent the paths from getting too long, we don't need after the 1st ":" for the folder
            var underscoreIndex = product.Title.IndexOf(':');
            var titleDir = (underscoreIndex < 4) ? product.Title : product.Title.Substring(0, underscoreIndex);
            var finalDir = FileUtility.GetValidFilename(AudibleFileStorage.BooksDirectory, titleDir, null, product.AudibleProductId);
            Directory.CreateDirectory(finalDir);

            // move audio files to the end of the collection so these files are moved last
            var musicFiles = files.Where(f => AudibleFileStorage.Audio.IsFileTypeMatch(f));
            files = files
                .Except(musicFiles)
                .Concat(musicFiles)
                .ToList();

            var musicFileExt = musicFiles
                .Select(f => f.Extension)
                .Distinct()
                .Single()
                .Trim('.');

            foreach (var f in files)
            {
                var dest = AudibleFileStorage.Audio.IsFileTypeMatch(f)
                    // audio filename: safetitle_limit50char + " [" + productId + "]." + audio_ext
                    ? FileUtility.GetValidFilename(finalDir, product.Title, musicFileExt, product.AudibleProductId)
                    // non-audio filename: safetitle_limit50char + " [" + productId + "][" + audio_ext +"]." + non_audio_ext
                    : FileUtility.GetValidFilename(finalDir, product.Title, f.Extension, product.AudibleProductId, musicFileExt);

                File.Move(f.FullName, dest);
            }
        }
    }
}