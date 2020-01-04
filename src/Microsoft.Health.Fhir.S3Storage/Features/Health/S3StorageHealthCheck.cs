// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Amazon;
using Amazon.S3;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.S3Storage.Configs;

namespace Microsoft.Health.Fhir.S3Storage.Features.Health
{
    /// <summary>
    /// An <see cref="IHealthCheck"/> implementation that verifies connectivity to the SQL database
    /// </summary>
    public class S3StorageHealthCheck : IHealthCheck
    {
        private readonly S3StorageDataStoreConfiguration _configuration;
        private readonly ILogger<S3StorageHealthCheck> _logger;
        private string _awsBucket;
        private string _awsKeyName;
        private System.Uri _awsHost;
        private AwsCredential _awsCredentials;

        // Specify your bucket region (an example region is shown).

        private static AwsRegion _bucketRegion;

        private static S3Client client;
        private string responseBody = string.Empty;

        public S3StorageHealthCheck(S3StorageDataStoreConfiguration configuration, ILogger<S3StorageHealthCheck> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _logger = logger;
            _awsBucket = "infrastructurefhir";
            _awsKeyName = "fhirserver";
            _awsHost = _configuration.S3StorageURL;
            _awsCredentials = new AwsCredential(_configuration.AuthentificationString, _configuration.SecretString);
            _bucketRegion = AwsRegion.EUCentral1;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            try
            {
                using (var connection = new SqlConnection(_configuration.ConnectionString))
                {
                    await connection.OpenAsync(cancellationToken);

                    SqlCommand command = connection.CreateCommand();
                    command.CommandText = "select @@DBTS";

                    await command.ExecuteScalarAsync(cancellationToken);
                }

                client = new S3Client(_bucketRegion, _awsCredentials);

                // ReadObjectDataAsync().Wait();
                GetObjectRequest request = new GetObjectRequest(_awsHost.ToString(), _awsBucket, _awsKeyName);

                using (S3Object response = await client.GetObjectAsync(request))
                {
                    var responseStream = response.OpenAsync();
                    using (StreamReader reader = new StreamReader(responseStream.Result))
                    {
                        // string title = response.Metadata["x-amz-meta-title"]; // Assume you have "title" as medata added to the object.
                        // string contentType = response.Headers["Content-Type"];
                        // Console.WriteLine("Object metadata, Title: {0}", title);
                        // Console.WriteLine("Content type: {0}", contentType);

                        responseBody = reader.ReadToEnd(); // Now you process the response body.
                        if (responseBody != "ok")
                        {
                            return HealthCheckResult.Healthy("Control Object for this servers says No for this server.");
                        }
                    }
                }

                return HealthCheckResult.Healthy("Successfully connected to the SQL and S3 Part of the data store.");
            }
            catch (S3Exception e)
            {
                _logger.LogWarning(e, "Failed to connect to the S3 data store.");
                return HealthCheckResult.Unhealthy("Failed to connect to the S3 store.");

                // ---------------------------
                // Console.WriteLine("Error encountered ***. Message:'{0}' when writing an object", e.Message);
                // --------------------------
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to the data store.");
                return HealthCheckResult.Unhealthy("Failed to connect to the data store.");
            }
        }
    }
}
