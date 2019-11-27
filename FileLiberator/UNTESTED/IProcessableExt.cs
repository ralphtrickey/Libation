﻿using System;
using System.Threading.Tasks;
using DataLayer;
using Dinah.Core.ErrorHandling;

namespace FileLiberator
{
    public static class IProcessableExt
    {
        //
        // DO NOT USE ConfigureAwait(false) WITH ProcessAsync() unless ensuring ProcessAsync() implementation is cross-thread compatible
        // ProcessAsync() often does a lot with forms in the UI context
        //


        /// <summary>Process the first valid product. Create default context</summary>
        /// <returns>Returns either the status handler from the process, or null if all books have been processed</returns>
        public static async Task<StatusHandler> ProcessFirstValidAsync(this IProcessable processable)
        {
            var libraryBook = processable.GetNextValid();
            if (libraryBook == null)
                return null;

			// this should never happen. check anyway. ProcessFirstValidAsync returning null is the signal that we're done. we can't let another IProcessable accidentally send this commans
			var status = await processable.ProcessAsync(libraryBook);
            if (status == null)
                throw new Exception("Processable should never return a null status");

            return status;
        }

        public static LibraryBook GetNextValid(this IProcessable processable)
        {
            var libraryBooks = LibraryQueries.GetLibrary_Flat_NoTracking();

            foreach (var libraryBook in libraryBooks)
                if (processable.Validate(libraryBook))
                    return libraryBook;

            return null;
        }

		public static async Task<StatusHandler> TryProcessAsync(this IProcessable processable, LibraryBook libraryBook)
			=> processable.Validate(libraryBook)
			? await processable.ProcessAsync(libraryBook)
			: new StatusHandler();
	}
}