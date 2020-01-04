﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.S3Storage.Configs;

namespace Microsoft.Health.Fhir.S3Storage.Features.Schema
{
    /// <summary>
    /// EXPERIMENTAL - Initializes the sql schema and brings the schema up to the min supported version.
    /// The purpose of this it to enable easy scenarios during development and will likely be removed later.
    /// </summary>
    public class S3SchemaInitializer : IStartable
    {
        private readonly S3StorageDataStoreConfiguration _s3ServerDataStoreConfiguration;
        private readonly S3SchemaUpgradeRunner _schemaUpgradeRunner;
        private readonly S3SchemaInformation _schemaInformation;
        private readonly ILogger<S3SchemaInitializer> _logger;

        public S3SchemaInitializer(S3StorageDataStoreConfiguration s3ServerDataStoreConfiguration, S3SchemaUpgradeRunner schemaUpgradeRunner, S3SchemaInformation schemaInformation, ILogger<S3SchemaInitializer> logger)
        {
            EnsureArg.IsNotNull(s3ServerDataStoreConfiguration, nameof(s3ServerDataStoreConfiguration));
            EnsureArg.IsNotNull(schemaUpgradeRunner, nameof(schemaUpgradeRunner));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _s3ServerDataStoreConfiguration = s3ServerDataStoreConfiguration;
            _schemaUpgradeRunner = schemaUpgradeRunner;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        private void Initialize()
        {
            if (!CanInitialize())
            {
                return;
            }

            GetCurrentSchemaVersion();

            _logger.LogInformation("Schema version is {version}", _schemaInformation.Current?.ToString() ?? "NULL");

            // GetCurrentVersion doesn't exist, so run version 1.
            if (_schemaInformation.Current == null || _s3ServerDataStoreConfiguration.DeleteAllDataOnStartup)
            {
                _schemaUpgradeRunner.ApplySchema(1);
                GetCurrentSchemaVersion();
            }

            if (_schemaInformation.Current < _schemaInformation.MaximumSupportedVersion)
            {
                int current = (int?)_schemaInformation.Current ?? 0;

                for (int i = current + 1; i <= (int)_schemaInformation.MaximumSupportedVersion; i++)
                {
                    _schemaUpgradeRunner.ApplySchema(i);
                }
            }
        }

        private void GetCurrentSchemaVersion()
        {
            using (var connection = new SqlConnection(_s3ServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                string procedureSchema = "dbo";
                string procedureName = "SelectCurrentSchemaVersion";

                bool procedureExists;
                using (var checkProcedureExistsCommand = connection.CreateCommand())
                {
                    checkProcedureExistsCommand.CommandText = @"
                        SELECT 1
                        FROM sys.procedures p
                        INNER JOIN sys.schemas s on p.schema_id = s.schema_id
                        WHERE s.name = @schemaName AND p.name = @procedureName";

                    checkProcedureExistsCommand.Parameters.AddWithValue("@schemaName", procedureSchema);
                    checkProcedureExistsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                    procedureExists = checkProcedureExistsCommand.ExecuteScalar() != null;
                }

                if (!procedureExists)
                {
                    _logger.LogInformation("Procedure to select the schema version was not found. This must be a new database.");
                }
                else
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"{procedureSchema}.{procedureName}";
                        command.CommandType = CommandType.StoredProcedure;
                        _schemaInformation.Current = (S3SchemaVersion?)(int?)command.ExecuteScalar();
                     }
                }
            }
        }

        private bool CanInitialize()
        {
            if (!_s3ServerDataStoreConfiguration.Initialize)
            {
                return false;
            }

            var configuredConnectionBuilder = new SqlConnectionStringBuilder(_s3ServerDataStoreConfiguration.ConnectionString);
            string databaseName = configuredConnectionBuilder.InitialCatalog;

            if (string.IsNullOrEmpty(databaseName))
            {
                throw new InvalidOperationException("The initial catalog must be specified in the connection string");
            }

            if (databaseName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
                databaseName.Equals("model", StringComparison.OrdinalIgnoreCase) ||
                databaseName.Equals("msdb", StringComparison.OrdinalIgnoreCase) ||
                databaseName.Equals("tempdb", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The initial catalog in the connection string cannot be a system database");
            }

            // connect to master database to evaluate if the requested database exists
            var masterConnectionBuilder = new SqlConnectionStringBuilder(_s3ServerDataStoreConfiguration.ConnectionString) { InitialCatalog = string.Empty };
            using (var connection = new SqlConnection(masterConnectionBuilder.ToString()))
            {
                connection.Open();

                using (var checkDatabaseExistsCommand = connection.CreateCommand())
                {
                    checkDatabaseExistsCommand.CommandText = "SELECT 1 FROM sys.databases where name = @databaseName";
                    checkDatabaseExistsCommand.Parameters.AddWithValue("@databaseName", databaseName);
                    bool exists = (int?)checkDatabaseExistsCommand.ExecuteScalar() == 1;

                    if (!exists)
                    {
                        _logger.LogInformation("Database does not exist");

                        using (var canCreateDatabaseCommand = new SqlCommand("SELECT count(*) FROM fn_my_permissions (NULL, 'DATABASE') WHERE permission_name = 'CREATE DATABASE'", connection))
                        {
                            if ((int)canCreateDatabaseCommand.ExecuteScalar() > 0)
                            {
                                using (var createDatabaseCommand = new SqlCommand($"CREATE DATABASE {databaseName}", connection))
                                {
                                    createDatabaseCommand.ExecuteNonQuery();
                                    _logger.LogInformation("Created database");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Insufficient permissions to create the database");
                                return false;
                            }
                        }
                    }
                }
            }

            // now switch to the target database

            using (var connection = new SqlConnection(_s3ServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                bool canInitialize;
                using (var command = new SqlCommand("SELECT count(*) FROM fn_my_permissions (NULL, 'DATABASE') WHERE permission_name = 'CREATE TABLE'", connection))
                {
                    canInitialize = (int)command.ExecuteScalar() > 0;
                }

                if (!canInitialize)
                {
                    _logger.LogWarning("Insufficient permissions to create tables in the database");
                }

                return canInitialize;
            }
        }

        public void Start()
        {
            if (!string.IsNullOrWhiteSpace(_s3ServerDataStoreConfiguration.ConnectionString))
            {
                Initialize();

                GetCurrentSchemaVersion();
            }
            else
            {
                _logger.LogCritical("There was no connection string supplied. Schema initialization can not be completed.");
            }
        }
    }
}
