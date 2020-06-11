// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Numerics;
using Hl7.Fhir.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.Health.Fhir.S3Storage.Features.Schema;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;
using NSubstitute;
using Polly;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class S3StorageFhirStorageTestsFixture : IServiceProvider, IAsyncLifetime
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";

        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly SqlServerFhirStorageTestHelper _testHelper;
        private readonly S3SchemaInitializer _schemaInitializer;

        public S3StorageFhirStorageTestsFixture()
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("S3Storage:ConnectionString") ?? LocalConnectionString;
            var secret = Environment.GetEnvironmentVariable("S3Storage:SecretString") ?? string.Empty;
            var authent = Environment.GetEnvironmentVariable("S3Storage:AuthentificationString") ?? string.Empty;
            var type = Environment.GetEnvironmentVariable("S3Storage:S3Type") ?? "AWS";
            var url = Environment.GetEnvironmentVariable("S3Storage:S3StorageURL") ?? "https://s3.us-west-2.amazonaws.com";
            var deleteOnStart = Environment.GetEnvironmentVariable("S3Storage:DeleteAllDataOnStartup") ?? "true";

            _databaseName = $"FHIRS3INTEGRATIONTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";
            _masterConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = "master" }.ToString();
            TestConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = _databaseName }.ToString();

            var config = new S3StorageDataStoreConfiguration { DeleteAllDataOnStartup = Convert.ToBoolean(deleteOnStart), ConnectionString = TestConnectionString, Initialize = true, AuthentificationString = authent, S3Type = type, S3StorageURL = new Uri(url), SecretString = secret };

            var schemaUpgradeRunner = new S3SchemaUpgradeRunner(config, NullLogger<S3SchemaUpgradeRunner>.Instance);

            var schemaInformation = new S3SchemaInformation();

            _schemaInitializer = new S3SchemaInitializer(config, schemaUpgradeRunner, schemaInformation, NullLogger<S3SchemaInitializer>.Instance);

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.AllSearchParameters.Returns(new[]
            {
                new SearchParameter { Name = SearchParameterNames.Id, Type = SearchParamType.Token, Url = SearchParameterNames.IdUri.ToString() }.ToInfo(),
                new SearchParameter { Name = SearchParameterNames.LastUpdated, Type = SearchParamType.Date, Url = SearchParameterNames.LastUpdatedUri.ToString() }.ToInfo(),
            });

            var securityConfiguration = new SecurityConfiguration { PrincipalClaims = { "oid" } };

            var sqlServerFhirModel = new S3StorageFhirModel(config, schemaInformation, searchParameterDefinitionManager, Options.Create(securityConfiguration), NullLogger<S3StorageFhirModel>.Instance);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddS3StorageTableRowParameterGenerators();
            serviceCollection.AddSingleton(sqlServerFhirModel);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var upsertResourceTvpGenerator = serviceProvider.GetRequiredService<S3Storage.Features.Schema.Model.V1.UpsertResourceTvpGenerator<S3ResourceMetadata>>();
            var upsertResourceLinkTvpGenerator = serviceProvider.GetRequiredService<S3Storage.Features.Schema.Model.V1.UpsertResourceLinkTvpGenerator<S3ResourceMetadata>>();
            var searchParameterToSearchValueTypeMap = new S3SearchParameterToSearchValueTypeMap(searchParameterDefinitionManager);

            SqlTransactionHandler = new S3SqlTransactionHandler();
            SqlConnectionWrapperFactory = new S3SqlConnectionWrapperFactory(config, SqlTransactionHandler);
            ILogger<S3StorageFhirDataStore> logger = NullLogger<S3StorageFhirDataStore>.Instance;
            _fhirDataStore = new S3StorageFhirDataStore(
                configuration: config,
                sqlServerFhirModel,
                searchParameterTypeMap: searchParameterToSearchValueTypeMap,
                upsertResourceTvpGenerator: upsertResourceTvpGenerator,
                upsertResourceLinkTvpGenerator: upsertResourceLinkTvpGenerator,
                coreFeatures: Options.Create(new CoreFeatureConfiguration()),
                sqlConnectionWrapperFactory: SqlConnectionWrapperFactory,
                logger: logger);
            _testHelper = new SqlServerFhirStorageTestHelper(TestConnectionString);
        }

        public string TestConnectionString { get; }

        internal S3SqlTransactionHandler SqlTransactionHandler { get; }

        internal S3SqlConnectionWrapperFactory SqlConnectionWrapperFactory { get; }

        public async Task InitializeAsync()
        {
            // Create the database
            using (var connection = new SqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"CREATE DATABASE {_databaseName}";
                    await command.ExecuteNonQueryAsync();
                }
            }

            // verify that we can connect to the new database. This sometimes does not work right away with Azure SQL.
            await Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 7,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .ExecuteAsync(async () =>
                {
                    using (var connection = new SqlConnection(TestConnectionString))
                    {
                        await connection.OpenAsync();
                        using (SqlCommand sqlCommand = connection.CreateCommand())
                        {
                            sqlCommand.CommandText = "SELECT 1";
                            await sqlCommand.ExecuteScalarAsync();
                        }
                    }
                });

            _schemaInitializer.Start();
        }

        public async Task DisposeAsync()
        {
            using (var connection = new SqlConnection(_masterConnectionString))
            {
                await connection.OpenAsync();
                SqlConnection.ClearAllPools();

                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandTimeout = 600;
                    command.CommandText = $"DROP DATABASE IF EXISTS {_databaseName}";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType == typeof(IFhirDataStore))
            {
                return _fhirDataStore;
            }

            if (serviceType == typeof(IFhirStorageTestHelper))
            {
                return _testHelper;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType == typeof(ITransactionHandler))
            {
                return SqlTransactionHandler;
            }

            return null;
        }
    }
}
