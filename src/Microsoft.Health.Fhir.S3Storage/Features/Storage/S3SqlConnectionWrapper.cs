// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Fhir.S3Storage.Configs;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage
{
    internal class S3SqlConnectionWrapper : IDisposable
    {
        private readonly bool _enlistInTransactionIfPresent;
        private readonly S3SqlTransactionHandler _sqlTransactionHandler;

        public S3SqlConnectionWrapper(S3StorageDataStoreConfiguration configuration, S3SqlTransactionHandler sqlTransactionHandler, bool enlistInTransactionIfPresent)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(sqlTransactionHandler, nameof(sqlTransactionHandler));

            _sqlTransactionHandler = sqlTransactionHandler;
            _enlistInTransactionIfPresent = enlistInTransactionIfPresent;

            if (_enlistInTransactionIfPresent && sqlTransactionHandler.S3SqlTransactionScope?.SqlConnection != null)
            {
                S3SqlConnection = sqlTransactionHandler.S3SqlTransactionScope.SqlConnection;
            }
            else
            {
                S3SqlConnection = new SqlConnection(configuration.ConnectionString);
            }

            if (_enlistInTransactionIfPresent && sqlTransactionHandler.S3SqlTransactionScope != null && sqlTransactionHandler.S3SqlTransactionScope.SqlConnection == null)
            {
                sqlTransactionHandler.S3SqlTransactionScope.SqlConnection = S3SqlConnection;
            }

            if (S3SqlConnection.State != ConnectionState.Open)
            {
                S3SqlConnection.Open();
            }

            if (enlistInTransactionIfPresent && sqlTransactionHandler.S3SqlTransactionScope != null)
            {
                S3SqlTransaction = sqlTransactionHandler.S3SqlTransactionScope.SqlTransaction ?? S3SqlConnection.BeginTransaction();

                if (sqlTransactionHandler.S3SqlTransactionScope.SqlTransaction == null)
                {
                    sqlTransactionHandler.S3SqlTransactionScope.SqlTransaction = S3SqlTransaction;
                }
            }
        }

        public SqlConnection S3SqlConnection { get; }

        public SqlTransaction S3SqlTransaction { get; }

        public SqlCommand CreateSqlCommand()
        {
            SqlCommand sqlCommand = S3SqlConnection.CreateCommand();
            sqlCommand.Transaction = S3SqlTransaction;

            return sqlCommand;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_enlistInTransactionIfPresent || _sqlTransactionHandler.S3SqlTransactionScope == null)
                {
                    S3SqlConnection?.Dispose();
                    S3SqlTransaction?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
