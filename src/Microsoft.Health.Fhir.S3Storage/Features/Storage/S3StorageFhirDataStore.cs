// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

// using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Model.Internal.MarshallTransformations;
using Amazon.S3.Transfer;
#pragma warning disable IDE0005 // Using-Direktive ist unnötig.
using Amazon.S3.Util;
#pragma warning restore IDE0005 // Using-Direktive ist unnötig.
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.S3Storage.Configs;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage
{
    /// <summary>
    /// A S3 Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class S3StorageFhirDataStore : IFhirDataStore, IProvideCapability
    {
        internal static readonly Encoding SQLResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        internal static readonly Encoding S3ResourceEncoding = new UTF8Encoding();

        private readonly S3StorageDataStoreConfiguration _configuration;
        private readonly S3StorageFhirModel _model;
        private readonly S3SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly V1.UpsertResourceLinkTvpGenerator<S3ResourceMetadata> _upsertResourceLinkTvpGenerator;
        private readonly V1.UpsertResourceTvpGenerator<S3ResourceMetadata> _upsertResourceTvpGenerator;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly S3SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<S3StorageFhirDataStore> _logger;
        private string _awsBucket;
        private System.Uri _awsHost;
        private RegionEndpoint bucketRegion = RegionEndpoint.USWest2;

        private static AmazonS3Client client = null;

        // private string responseBody = string.Empty;

        public S3StorageFhirDataStore(
            S3StorageDataStoreConfiguration configuration,
            S3StorageFhirModel model,
            S3SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            V1.UpsertResourceTvpGenerator<S3ResourceMetadata> upsertResourceTvpGenerator,
            V1.UpsertResourceLinkTvpGenerator<S3ResourceMetadata> upsertResourceLinkTvpGenerator,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            S3SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<S3StorageFhirDataStore> logger)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            EnsureArg.IsNotNull(upsertResourceTvpGenerator, nameof(upsertResourceTvpGenerator));
            EnsureArg.IsNotNull(upsertResourceLinkTvpGenerator, nameof(upsertResourceLinkTvpGenerator));
            EnsureArg.IsNotNull(coreFeatures, nameof(coreFeatures));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _configuration = configuration;
            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _upsertResourceTvpGenerator = upsertResourceTvpGenerator;
            _upsertResourceLinkTvpGenerator = upsertResourceLinkTvpGenerator;
            _upsertResourceTvpGenerator = upsertResourceTvpGenerator;
            _coreFeatures = coreFeatures.Value;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _configuration = configuration;
            _logger = logger;
            _memoryStreamManager = new RecyclableMemoryStreamManager();

            // awsCredentials = new AwsCredential(_configuration.AuthentificationString, _configuration.SecretString);
            if (_configuration.S3Type == "AWS")
            {
                bucketRegion = RegionEndpoint.EUCentral1;
            }
            else if (_configuration.S3Type == "MINIO")
            {
                bucketRegion = RegionEndpoint.USEast1;
            }
            else
            {
                bucketRegion = RegionEndpoint.USWest2;
            }

            _awsHost = _configuration.S3StorageURL;

            if (!string.IsNullOrEmpty(_configuration.S3Instance))
            {
                _awsBucket = string.Concat("instance-", _configuration.S3Instance.ToString());
            }
            else
            {
                _awsBucket = string.Concat("instance-", "no-instance");
            }

            // awsBucket = "kfhfhir1";
        }

        // make bucket publicly readable

        public static async Task CreateBucketAsync(string bucket, AmazonS3Client client, ILogger logger, CancellationToken token)
        {
            if (string.IsNullOrEmpty(bucket))
            {
                return;
            }

            try
            {
                if (await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
                {
                    return;
                }

                await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket, UseClientRegion = true }, token);
            }
            catch (Exception e)
            {
                logger.LogDebug("Error storing S3 Object Exception {Exception}", e.ToString());
            }

            // await S3.SetMultiPartLifetime(client, bucket, token);
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            string linktoRawResource = null;
            bool isS3Objectvalid = false;
            int etag = 0;
            if (weakETag != null && !int.TryParse(weakETag.VersionId, out etag))
            {
                // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.
                etag = -1;
            }

            var resourceMetadata = new S3ResourceMetadata(
                resource.CompartmentIndices,
                resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.LastModifiedClaims);

            // Hans Code
            client = CreatClient(_awsBucket, _configuration, _logger);

            // Hans Code ende
            // Alter Code
            // client = new S3Client(_awsbucketRegion, _awsCredentials);

            // Alter Code ende

            linktoRawResource = string.Concat(resource.ResourceTypeName, "/");
            linktoRawResource = string.Concat(linktoRawResource, resource.ResourceId);
            linktoRawResource = string.Concat(linktoRawResource, "-" + Guid.NewGuid().ToString());

            // linktoRawResource = resource.ResourceId;
            string awsKeyName = linktoRawResource;
            Console.WriteLine("Store Object on Cloudian");
            Console.WriteLine("Host");
            Console.WriteLine(client.Config.ServiceURL);
            Console.WriteLine("Object Name");
            Console.WriteLine(linktoRawResource);
            Console.WriteLine("Usehttp");
            Console.WriteLine(client.Config.UseHttp.ToString());

            using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
            using (var writer = new StreamWriter(stream, S3ResourceEncoding))
            {
                // Neu
                writer.Write(resource.RawResource.Data);
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

                try
                {
                    TransferUtility fileTransferUtility = new TransferUtility(client);

                    fileTransferUtility.Upload(fileTransferUtilityRequest);

                    // Ende Neu
                    // Alter code
                    // PutObjectRequest request = new PutObjectRequest(client.Host, _awsBucket, awsKeyName);
                    //  PutObjectResult response = null;
                    // RawResource jsonresource = new RawResource(resource.RawResource.Data, FhirResourceFormat.Json);

                    // using (var writer = new StreamWriter(stream, S3ResourceEncoding))
                    // {
                    //     writer.Write(jsonresource.Data);
                    //    writer.Flush();

                    // stream.Seek(0, 0);
                    // request.SetStream(stream);
                    // try
                    // {
                    //  response = await client.PutObjectAsync(request);
                    isS3Objectvalid = true;
                }
                catch (Exception e)
                {
                    _logger.LogDebug("Error storing S3 Object Exception {Exception}", e.ToString());
                    isS3Objectvalid = false;
                }
            }

            using (S3SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                using (SqlCommand command = sqlConnectionWrapper.CreateSqlCommand())
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzipStream, SQLResourceEncoding))
                {
                    writer.Write(resource.RawResource.Data);
                    writer.Flush();

                    stream.Seek(0, 0);

                    if (isS3Objectvalid)
                    {
                        V1.UpsertResourceLink.PopulateCommand(
                            command,
                            baseResourceSurrogateId: S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime),
                            resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                            resourceId: resource.ResourceId,
                            eTag: weakETag == null ? null : (int?)etag,
                            allowCreate: allowCreate,
                            isDeleted: resource.IsDeleted,
                            keepHistory: keepHistory,
                            requestMethod: resource.Request.Method,
                            linkToRawResource: linktoRawResource,
                            isS3ObjectValid: isS3Objectvalid,
                            tableValuedParameters: _upsertResourceLinkTvpGenerator.Generate(resourceMetadata));
                    }
                    else
                    {
                        V1.UpsertResource.PopulateCommand(
                            command,
                            baseResourceSurrogateId: S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime),
                            resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                            resourceId: resource.ResourceId,
                            eTag: weakETag == null ? null : (int?)etag,
                            allowCreate: allowCreate,
                            isDeleted: resource.IsDeleted,
                            keepHistory: keepHistory,
                            requestMethod: resource.Request.Method,
                            rawResource: stream,
                            isS3ObjectValid: isS3Objectvalid,
                            tableValuedParameters: _upsertResourceTvpGenerator.Generate(resourceMetadata));
                    }

                    try
                    {
                        var newVersion = (int?)await command.ExecuteScalarAsync(cancellationToken);
                        if (newVersion == null)
                        {
                            // indicates a redundant delete
                            return null;
                        }

                        resource.Version = newVersion.ToString();

                        return new UpsertOutcome(resource, newVersion == 1 ? SaveOutcomeType.Created : SaveOutcomeType.Updated);
                    }
                    catch (SqlException e)
                    {
                        switch (e.Number)
                        {
                            case S3SqlErrorCodes.PreconditionFailed:
                                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag?.VersionId));
                            case S3SqlErrorCodes.NotFound:
                                if (weakETag != null)
                                {
                                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                                }

                                goto default;
                            case S3SqlErrorCodes.MethodNotAllowed:
                                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                            default:
                                _logger.LogError(e, "Error from SQL database on upsert");
                                throw;
                        }
                    }
                }
            }
        }

        public static AmazonS3Client CreatClient(string bucket, S3StorageDataStoreConfiguration configuration, ILogger logger)
        {
            AmazonS3Client client = CreatS3Client(configuration);
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            Task bCreate = CreateBucketAsync(bucket, client, logger, token);
            bCreate.Wait();
            return client;
        }

        public static AmazonS3Client CreatS3Client(S3StorageDataStoreConfiguration configuration)
        {
            RegionEndpoint bucketRegion;
            AmazonS3Config s3Config = new AmazonS3Config();

            if (configuration.S3Type == "AWS")
            {
                bucketRegion = RegionEndpoint.EUCentral1;
                s3Config.RegionEndpoint = bucketRegion;
                s3Config.ServiceURL = "https://s3.eu-central-1.amazonaws.com";
            }
            else
            {
                if (configuration.S3Type == "MINIO")
                {
                    bucketRegion = RegionEndpoint.USEast1;
                    s3Config.RegionEndpoint = bucketRegion;
                    s3Config.ForcePathStyle = true;
                }
                else
                {
                    s3Config.RegionEndpoint = RegionEndpoint.USWest2;
                }

                s3Config.ServiceURL = configuration.S3StorageURL.ToString();
            }

            s3Config.UseHttp = true;
            if (client == null)
            {
                client = new AmazonS3Client(configuration.AuthentificationString, configuration.SecretString, s3Config);
            }

            return client;
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            using (S3SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                Console.WriteLine("Read Object");

                // await connection.OpenAsync(cancellationToken);

                int? requestedVersion = null;
                if (!string.IsNullOrEmpty(key.VersionId))
                {
                    if (!int.TryParse(key.VersionId, out var parsedVersion))
                    {
                        return null;
                    }

                    requestedVersion = parsedVersion;
                }

                using (SqlCommand command = sqlConnectionWrapper.CreateSqlCommand())
                {
                    V1.ReadResource.PopulateCommand(
                        command,
                        resourceTypeId: _model.GetResourceTypeId(key.ResourceType),
                        resourceId: key.Id,
                        version: requestedVersion);

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (!sqlDataReader.Read())
                        {
                            return null;
                        }

                        var resourceTable = V1.Resource;

                        (long resourceSurrogateId, int version, bool isDeleted, bool isHistory, Stream rawResourceStream,  bool isS3ObjectValid, string linkToRawResource) = sqlDataReader.ReadRow(
                            resourceTable.ResourceSurrogateId,
                            resourceTable.Version,
                            resourceTable.IsDeleted,
                            resourceTable.IsHistory,
                            resourceTable.RawResource,
                            resourceTable.IsS3ObjectValid,
                            resourceTable.LinkToRawResource);

                        string rawResource = null;
                        try
                        {
                            if (isS3ObjectValid)
                            {
                               Console.WriteLine("S3 Object Valid");
                               Console.WriteLine(resourceTable.LinkToRawResource);
                               client = CreatClient(_awsBucket, _configuration, _logger);
                               using (GetObjectResponse response = await client.GetObjectAsync(_awsBucket, linkToRawResource))
                               using (Stream responseStream = response.ResponseStream)
                               using (StreamReader reader = new StreamReader(responseStream, S3ResourceEncoding))
                               {
                                    // The following outputs the content of my text file:
                                    // string content = reader.ReadToEnd();
                                    // client = new S3Client(_awsbucketRegion, _awsCredentials);
                                    // GetObjectRequest request = new GetObjectRequest(client.Host.ToString(), _awsBucket, linkToRawResource);

                                    // using (S3Object response = await client.GetObjectAsync(request))
                               // {
                                   //  var responseStream = response.OpenAsync();
                                   //  using (StreamReader reader = new StreamReader(responseStream.Result, S3ResourceEncoding))
                                    // {
                                        rawResource = reader.ReadToEnd(); // Now you process the response body.

                                    // }
                               }
                            }
                            else
                            {
                                Console.WriteLine("S3 Object not valid, get Data from SQL Database");

                                using (rawResourceStream)
                                using (var gzipStream = new GZipStream(rawResourceStream, CompressionMode.Decompress))
                                using (var reader = new StreamReader(gzipStream, SQLResourceEncoding))
                                {
                                    rawResource = await reader.ReadToEndAsync();
                                }
                            }
                        }
                        catch (AmazonS3Exception e)
                        {
                            _logger.LogWarning(e, "Failed to read to the S3 data {Exception}", e.Message);
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning(e, "Failed to read to the SQL data {Exception}", e.Message);
                        }

                        return new ResourceWrapper(
                            key.Id,
                            version.ToString(CultureInfo.InvariantCulture),
                            key.ResourceType,
                            new RawResource(rawResource, FhirResourceFormat.Json),
                            null,
                            new DateTimeOffset(S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                            isDeleted,
                            searchIndices: null,
                            compartmentIndices: null,
                            lastModifiedClaims: null)
                        {
                            IsHistory = isHistory,
                        };
                    }
                }
            }
        }

        public async Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            await _model.EnsureInitialized();

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                {
                    V1.ReadResourceLink.PopulateCommand(
                        command,
                        resourceTypeId: _model.GetResourceTypeId(key.ResourceType),
                        resourceId: key.Id,
                        null);

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (sqlDataReader.Read())
                        {
                            var resourceTable = V1.Resource;

                            (long resourceSurrogateId, int version, bool isDeleted, bool isHistory, bool isS3ObjectValid, string linkToRawResource) = sqlDataReader.ReadRow(
                                resourceTable.ResourceSurrogateId,
                                resourceTable.Version,
                                resourceTable.IsDeleted,
                                resourceTable.IsHistory,
                                resourceTable.IsS3ObjectValid,
                                resourceTable.LinkToRawResource);

                            try
                            {
                                // if (isS3ObjectValid)
                                // {
                                //    client = new S3Client(_awsbucketRegion, _awsCredentials);
                                //    DeleteObjectRequest request = new DeleteObjectRequest(client.Host.ToString(), _awsBucket, linkToRawResource);
                                await client.DeleteObjectAsync(_awsBucket, linkToRawResource);

                                // await client.DeleteObjectAsync(request);
                                // }
                                }
                            catch (AmazonS3Exception e)
                            {
                                _logger.LogWarning(e, "Failed to read to the S3 data {Exception}", e.Message);
                            }
                        }
                    }
                }
            }

            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var command = connection.CreateCommand())
                {
                    V1.HardDeleteResource.PopulateCommand(command, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.AddDefaultResourceInteractions()
                   .AddDefaultSearchParameters()
                   .AddDefaultRestSearchParams();

            if (_coreFeatures.SupportsBatch)
            {
                // Batch supported added in listedCapability
                builder.AddRestInteraction(SystemRestfulInteraction.Batch);
            }

            if (_coreFeatures.SupportsTransaction)
            {
                // Transaction supported added in listedCapability
                builder.AddRestInteraction(SystemRestfulInteraction.Transaction);
            }
        }
    }
}
