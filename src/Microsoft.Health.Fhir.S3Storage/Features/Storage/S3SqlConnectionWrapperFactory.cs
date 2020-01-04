// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.S3Storage.Configs;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage
{
    internal class S3SqlConnectionWrapperFactory
    {
        private readonly S3StorageDataStoreConfiguration _configuration;
        private readonly S3SqlTransactionHandler _sqlTransactionHandler;

        public S3SqlConnectionWrapperFactory(S3StorageDataStoreConfiguration configuration, S3SqlTransactionHandler sqlTransactionHandler)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));

            _configuration = configuration;
            _sqlTransactionHandler = sqlTransactionHandler;
        }

        public S3SqlConnectionWrapper ObtainSqlConnectionWrapper(bool enlistInTransaction = false)
        {
            return new S3SqlConnectionWrapper(_configuration, _sqlTransactionHandler, enlistInTransaction);
        }
    }
}
