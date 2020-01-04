// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage
{
    internal class S3SqlTransactionHandler : ITransactionHandler
    {
        public S3SqlTransactionScope S3SqlTransactionScope { get; private set; }

        public ITransactionScope BeginTransaction()
        {
            Debug.Assert(S3SqlTransactionScope == null, "The existing SQL transaction scope should be completed before starting a new transaction.");

            if (S3SqlTransactionScope != null)
            {
                throw new TransactionFailedException(Resources.TransactionProcessingException, HttpStatusCode.InternalServerError);
            }

            S3SqlTransactionScope = new S3SqlTransactionScope(this);

            return S3SqlTransactionScope;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                S3SqlTransactionScope?.Dispose();

                S3SqlTransactionScope = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
