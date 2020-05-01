// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.S3Storage.Api.Controllers;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.Health.Fhir.S3Storage.Features.Health;
using Microsoft.Health.Fhir.S3Storage.Features.Schema;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;
using Microsoft.Health.Fhir.S3Storage.Features.Search;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderS3StorageRegistrationExtensions
    {
        public static IFhirServerBuilder AddExperimentalS3Storage(this IFhirServerBuilder fhirServerBuilder, Action<S3StorageDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add(provider =>
                {
                    var config = new S3StorageDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("S3Storage").Bind(config);
                    configureAction?.Invoke(config);

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<S3SchemaUpgradeRunner>()
                .Singleton()
                .AsSelf();

            services.Add<S3SchemaInformation>()
                .Singleton()
                .AsSelf();

            services.Add<S3SchemaInitializer>()
                .Singleton()
                .AsService<IStartable>();

            services.Add<S3StorageFhirModel>()
                .Singleton()
                .AsSelf();

            services.Add<S3SearchParameterToSearchValueTypeMap>()
                .Singleton()
                .AsSelf();

            services.Add<S3StorageFhirDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<S3SqlTransactionHandler>()
                 .Scoped()
                 .AsSelf()
                 .AsImplementedInterfaces();

            services.Add<S3SqlConnectionWrapperFactory>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<S3StorageFhirOperationDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<S3StorageSearchService>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services
                .AddHealthChecks()
                .AddCheck<S3StorageHealthCheck>(nameof(S3StorageHealthCheck));

            // This is only needed while adding in the ConfigureServices call in the E2E TestServer scenario
            // During normal usage, the controller should be automatically discovered.
            services.AddMvc().AddApplicationPart(typeof(S3SchemaController).Assembly);

            AddS3StorageTableRowParameterGenerators(services);

            services.Add<S3NormalizedSearchParameterQueryGeneratorFactory>()
                .Singleton()
                .AsSelf();

            services.Add<S3SqlRootExpressionRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<S3ChainFlatteningRewriter>()
                .Singleton()
                .AsSelf();

            services.Add<S3StringOverflowRewriter>()
                .Singleton()
                .AsSelf();

            return fhirServerBuilder;
        }

        internal static void AddS3StorageTableRowParameterGenerators(this IServiceCollection serviceCollection)
        {
            foreach (var type in typeof(S3StorageFhirDataStore).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                foreach (var interfaceType in type.GetInterfaces())
                {
                    if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IStoredProcedureTableValuedParametersGenerator<,>))
                    {
                        serviceCollection.AddSingleton(type);
                    }

                    if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(ITableValuedParameterRowGenerator<,>))
                    {
                        serviceCollection.Add(type).Singleton().AsSelf().AsService(interfaceType);
                    }
                }
            }
        }
    }
}
