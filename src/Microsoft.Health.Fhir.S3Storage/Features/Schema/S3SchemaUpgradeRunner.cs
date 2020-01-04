// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Microsoft.Health.Fhir.S3Storage.Features.Schema
{
    public class S3SchemaUpgradeRunner
    {
        private readonly S3StorageDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly ILogger<S3SchemaUpgradeRunner> _logger;

        public S3SchemaUpgradeRunner(S3StorageDataStoreConfiguration sqlServerDataStoreConfiguration, ILogger<S3SchemaUpgradeRunner> logger)
        {
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _logger = logger;
        }

        public void ApplySchema(int version)
        {
            _logger.LogInformation("Applying schema {version}", version);

            if (version != 1)
            {
                InsertSchemaVersion(version);
            }

            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();
                var server = new Server(new ServerConnection(connection));
                server.ConnectionContext.ExecuteNonQuery(GetMigrationScript(version));
            }

            CompleteSchemaVersion(version);

            _logger.LogInformation("Completed applying schema {version}", version);
        }

        private static string GetMigrationScript(int version)
        {
            string resourceName = $"{typeof(S3SchemaUpgradeRunner).Namespace}.Migrations.{version}.sql";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void InsertSchemaVersion(int schemaVersion)
        {
            UpsertSchemaVersion(schemaVersion, "started");
        }

        private void CompleteSchemaVersion(int schemaVersion)
        {
            UpsertSchemaVersion(schemaVersion, "complete");
        }

        private void UpsertSchemaVersion(int schemaVersion, string status)
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var upsertCommand = new SqlCommand("dbo.UpsertSchemaVersion", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                upsertCommand.Parameters.AddWithValue("@version", schemaVersion);
                upsertCommand.Parameters.AddWithValue("@status", status);

                connection.Open();
                upsertCommand.ExecuteNonQuery();
            }
        }
    }
}
