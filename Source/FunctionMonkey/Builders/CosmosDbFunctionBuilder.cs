﻿using System.Collections.Generic;
using AzureFromTheTrenches.Commanding.Abstractions;
using FunctionMonkey.Abstractions.Builders;
using FunctionMonkey.Commanding.Cosmos.Abstractions;
using FunctionMonkey.Extensions;
using FunctionMonkey.Model;

namespace FunctionMonkey.Builders
{
    internal class CosmosDbFunctionBuilder : ICosmosDbFunctionBuilder
    {
        private readonly string _connectionStringName;
        private readonly string _leaseConnectionStringName;
        private readonly List<AbstractFunctionDefinition> _functionDefinitions;

        public CosmosDbFunctionBuilder(string connectionStringName,
            string leaseConnectionName,
            List<AbstractFunctionDefinition> functionDefinitions)
        {
            _connectionStringName = connectionStringName;
            _functionDefinitions = functionDefinitions;
            _leaseConnectionStringName = leaseConnectionName;
        }

        public ICosmosDbFunctionOptionBuilder ChangeFeedFunction<TCommand>(
            string collectionName,
            string databaseName,
            string leaseCollectionName = "leases",
            string leaseDatabaseName = null,
            bool createLeaseCollectionIfNotExists = false,
            bool startFromBeginning = false,
            bool convertToPascalCase = true,
            string leaseCollectionPrefix = null,
            int? maxItemsPerInvocation = null,
            int? feedPollDelay = null,
            int? leaseAcquireInterval = null,
            int? leaseExpirationInterval = null,
            int? leaseRenewInterval = null,
            int? checkpointFrequency = null,
            int? leasesCollectionThroughput = null
            ) where TCommand : ICommand
        {
            CosmosDbFunctionDefinition definition = new CosmosDbFunctionDefinition(typeof(TCommand))
            {
                ConnectionStringName = _connectionStringName,
                CollectionName = collectionName,
                DatabaseName = databaseName,
                LeaseConnectionStringName = _leaseConnectionStringName,
                LeaseCollectionName = leaseCollectionName,
                LeaseDatabaseName = leaseDatabaseName ?? databaseName,
                CreateLeaseCollectionIfNotExists = createLeaseCollectionIfNotExists,
                ConvertToPascalCase = convertToPascalCase,
                StartFromBeginning = startFromBeginning,
                LeaseCollectionPrefix = leaseCollectionPrefix,
                MaxItemsPerInvocation = maxItemsPerInvocation,
                FeedPollDelay = feedPollDelay,
                LeaseAcquireInterval = leaseAcquireInterval,
                LeaseExpirationInterval = leaseExpirationInterval,
                LeaseRenewInterval = leaseRenewInterval,
                CheckpointFrequency = checkpointFrequency,
                LeasesCollectionThroughput = leasesCollectionThroughput
            };
            _functionDefinitions.Add(definition);
            return new CosmosDbFunctionOptionBuilder(this, definition);
        }

        public ICosmosDbFunctionOptionBuilder ChangeFeedFunction<TCommand, TCosmosDbErrorHandler>(
            string collectionName,
            string databaseName,
            string leaseCollectionName = "leases",
            string leaseDatabaseName = null,
            bool createLeaseCollectionIfNotExists = false,
            bool startFromBeginning = false,
            bool convertToPascalCase = false,
            string leaseCollectionPrefix = null,
            int? maxItemsPerInvocation = null,
            int? feedPollDelay = null,
            int? leaseAcquireInterval = null,
            int? leaseExpirationInterval = null,
            int? leaseRenewInterval = null,
            int? checkpointFrequency = null,
            int? leasesCollectionThroughput = null) where TCommand : ICommand where TCosmosDbErrorHandler : ICosmosDbErrorHandler
        {
            CosmosDbFunctionDefinition definition = new CosmosDbFunctionDefinition(typeof(TCommand))
            {
                ConnectionStringName = _connectionStringName,
                CollectionName = collectionName,
                DatabaseName = databaseName,
                LeaseConnectionStringName = _leaseConnectionStringName,
                LeaseCollectionName = leaseCollectionName,
                LeaseDatabaseName = leaseDatabaseName ?? databaseName,
                CreateLeaseCollectionIfNotExists = createLeaseCollectionIfNotExists,
                ConvertToPascalCase = convertToPascalCase,
                StartFromBeginning = startFromBeginning,
                LeaseCollectionPrefix = leaseCollectionPrefix,
                MaxItemsPerInvocation = maxItemsPerInvocation,
                FeedPollDelay = feedPollDelay,
                LeaseAcquireInterval = leaseAcquireInterval,
                LeaseExpirationInterval = leaseExpirationInterval,
                LeaseRenewInterval = leaseRenewInterval,
                CheckpointFrequency = checkpointFrequency,
                LeasesCollectionThroughput = leasesCollectionThroughput,
                ErrorHandlerType = typeof(TCosmosDbErrorHandler),
                ErrorHandlerTypeName = typeof(TCosmosDbErrorHandler).EvaluateType()
            };
            _functionDefinitions.Add(definition);
            return new CosmosDbFunctionOptionBuilder(this, definition);
        }
    }
}
