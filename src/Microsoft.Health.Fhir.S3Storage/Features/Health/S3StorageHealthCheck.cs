// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

// using System.IO;
// using System.Data;

using System.Data.SqlClient;
using System.IO;
using System.Text;

// using System.IO;
using System.Threading;
using System.Threading.Tasks;

// using Amazon;

// using Amazon.Runtime;
using Amazon.S3;

using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.S3.Util;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.S3Storage.Features.Health
{
    /// <summary>
    /// An <see cref="IHealthCheck"/> implementation that verifies connectivity to the SQL database
    /// </summary>
    public class S3StorageHealthCheck : IHealthCheck
    {
        private readonly S3StorageDataStoreConfiguration _configuration;
        private readonly ILogger<S3StorageHealthCheck> _logger;
        private bool s3Valid = false;
        private bool sqlValid = false;
        private string _awsBucket = string.Empty;
        private static AmazonS3Client client = null;
        private static readonly Encoding S3ResourceEncoding = new UTF8Encoding();
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private string time = string.Empty;

        // private string _awsKeyName = "";
        // private System.Uri _awsHost;

        // private AwsCredential _awsCredentials;
        // private static AmazonS3Client client = null;
        // private string responseBody = string.Empty;

        public S3StorageHealthCheck(S3StorageDataStoreConfiguration configuration, ILogger<S3StorageHealthCheck> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(logger, nameof(logger));
            s3Valid = false;
            sqlValid = false;

            _configuration = configuration;
            _logger = logger;

            if (!string.IsNullOrEmpty(_configuration.S3Instance))
            {
                _awsBucket = string.Concat("instance-", _configuration.S3Instance.ToString());
            }
            else
            {
                _awsBucket = string.Concat("instance-", "no-instance");
            }

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            // _awsKeyName = "fhirserver";
            // _awsHost = _configuration.S3StorageURL;

            // _awsCredentials = new AwsCredential(_configuration.AuthentificationString, _configuration.SecretString);
            // if (_configuration.S3Type == "AWS")
            // {
            //    _bucketRegion = AwsRegion.EUCentral1;
            // }
            // else
            // {
            //    _bucketRegion = AwsRegion.USWest2;
            // }
        }

        public async Task CreateBucketAsync(string bucket, CancellationToken token)
        {
            if (string.IsNullOrEmpty(bucket))
            {
                return;
            }

            try
            {
                client = S3StorageFhirDataStore.CreatClient(_awsBucket, _configuration, _logger);
                if (await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
                {
                    return;
                }

                await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket, UseClientRegion = true }, token);
            }
            catch (Exception e)
            {
                _logger.LogDebug("Error storing S3 Object Exception {Exception}", e.ToString());
            }

            // await S3.SetMultiPartLifetime(client, bucket, token);
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

                sqlValid = true;
                //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                client = S3StorageFhirDataStore.CreatClient(_awsBucket, _configuration, _logger);
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;
                await CreateBucketAsync(_awsBucket, token);

                time = DateTime.Now.ToString("dd-mm-yyy hh:mm:ss");
                Console.WriteLine("The current time is {0}", time);
                string awsKeyName = string.Concat("healh-check-", time);
                Console.WriteLine("Write Health Cheek TimeStamp to Cloudian");
                Console.WriteLine("Host");
                Console.WriteLine(client.Config.ServiceURL);
                Console.WriteLine("Object Name");
                Console.WriteLine(awsKeyName);
                Console.WriteLine("Usehttp");
                Console.WriteLine(client.Config.UseHttp.ToString());

                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                using (var writer = new StreamWriter(stream, S3ResourceEncoding))
                {
                    // Neu
                    time = DateTime.Now.ToString("dd-mm-yyy hh:mm:ss");
                    writer.Write(time);
                    writer.Flush();
                    stream.Seek(0, 0);

                    var fileTransferUtilityRequest = new TransferUtilityUploadRequest
                    {
                        BucketName = _awsBucket,
                        InputStream = stream,
                        StorageClass = S3StorageClass.Standard,
                        PartSize = 62914560, // 6 MB.
                        Key = awsKeyName,

                        // CannedACL = S3CannedACL.PublicRead,

                        ContentType = "application/json",
                    };

                    TransferUtility fileTransferUtility = new TransferUtility(client);

                    fileTransferUtility.Upload(fileTransferUtilityRequest);
                    s3Valid = true;
                }
            }
            catch (AmazonS3Exception e)
            {
                _logger.LogWarning(e, "Failed to connect to the S3 data store.");
                s3Valid = false;

                return HealthCheckResult.Unhealthy("Failed to connect to the S3 store.");

                // ---------------------------
                // Console.WriteLine("Error encountered ***. Message:'{0}' when writing an object", e.Message);
                // --------------------------
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to the data store.");
                s3Valid = false;
                sqlValid = false;
                return HealthCheckResult.Unhealthy("Failed to connect to the data store.");
            }
            finally
            {
            }

            if (s3Valid && sqlValid)
            {
                return HealthCheckResult.Healthy("Successfully connected to the SQL and S3 Part of the data store.");
            }
            else
            {
                return HealthCheckResult.Unhealthy("Failed to connect to the data store.");
            }
        }
    }
}
