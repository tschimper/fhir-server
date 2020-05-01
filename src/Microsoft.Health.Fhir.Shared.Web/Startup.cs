﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Azure;

namespace Microsoft.Health.Fhir.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            Console.WriteLine("startup conf:");
            Console.WriteLine(string.Empty, Configuration);
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddDevelopmentIdentityProvider(Configuration);

            Core.Registration.IFhirServerBuilder fhirServerBuilder = services.AddFhirServer(Configuration)
                .AddExportWorker()
                .AddKeyVaultSecretStore(Configuration)
                .AddAzureExportDestinationClient();

            string dataStore = Configuration["DataStore"];
            Console.WriteLine("datastore  found:" + dataStore);
            if (dataStore.Equals(KnownDataStores.CosmosDb, StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine("add Storage CosmosDB");
                fhirServerBuilder.AddCosmosDb(Configuration);
            }
            else if (dataStore.Equals(KnownDataStores.SqlServer, StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine("add Storage SQLServer");
                fhirServerBuilder.AddExperimentalSqlServer();
            }
            else if (dataStore.Equals(KnownDataStores.S3Storage, StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine("add Storage S3Storage");
                fhirServerBuilder.AddExperimentalS3Storage();
            }

            AddApplicationInsightsTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            Console.WriteLine("app.UseFhirServer");
            app.UseFhirServer();

            app.UseDevelopmentIdentityProvider();
        }

        /// <summary>
        /// Adds ApplicationInsights for telemetry and logging.
        /// </summary>
        private void AddApplicationInsightsTelemetry(IServiceCollection services)
        {
            string instrumentationKey = Configuration["ApplicationInsights:InstrumentationKey"];

            Console.WriteLine("AddApplicationInsightsTelemetry");

            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                services.AddApplicationInsightsTelemetry(instrumentationKey);
                services.AddLogging(loggingBuilder => loggingBuilder.AddApplicationInsights(instrumentationKey));
            }
        }
    }
}
