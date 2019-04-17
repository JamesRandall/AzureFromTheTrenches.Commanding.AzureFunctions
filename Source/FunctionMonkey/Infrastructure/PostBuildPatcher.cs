﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using AzureFromTheTrenches.Commanding.Abstractions;
using FunctionMonkey.Abstractions.Builders;
using FunctionMonkey.Abstractions.Builders.Model;
using FunctionMonkey.Abstractions.Extensions;
using FunctionMonkey.Builders;
using FunctionMonkey.Commanding.Abstractions;
using FunctionMonkey.Commanding.Abstractions.Validation;
using FunctionMonkey.Commanding.Cosmos.Abstractions;
using FunctionMonkey.Extensions;
using FunctionMonkey.Model;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace FunctionMonkey.Infrastructure
{
    public class PostBuildPatcher
    {
        public void Patch(FunctionHostBuilder builder, string newAssemblyNamespace)
        {
            AuthorizationBuilder authorizationBuilder = (AuthorizationBuilder) builder.AuthorizationBuilder;
            Type validationResultType = typeof(ValidationResult);
            
            foreach (AbstractFunctionDefinition definition in builder.FunctionDefinitions)
            {
                definition.Namespace = newAssemblyNamespace;
                definition.IsUsingValidator = builder.ValidatorType != null;

                definition.CommandDeserializerType = definition.CommandDeserializerType ??
                                                     ((SerializationBuilder)(builder.SerializationBuilder)).DefaultCommandDeserializerType;
                
                if (definition is HttpFunctionDefinition httpFunctionDefinition)
                {
                    CompleteHttpFunctionDefinition(builder, httpFunctionDefinition, authorizationBuilder, validationResultType);
                }
                else if (definition is CosmosDbFunctionDefinition cosmosDbFunctionDefinition)
                {
                    CompleteCosmosDbFunctionDefinition(cosmosDbFunctionDefinition);
                }
            }
        }

        private void CompleteCosmosDbFunctionDefinition(CosmosDbFunctionDefinition cosmosDbFunctionDefinition)
        {
            Type documentCommandType = typeof(ICosmosDbDocumentCommand);
            Type documentBatchCommandType = typeof(ICosmosDbDocumentBatchCommand);

            ExtractCosmosCommandProperties(cosmosDbFunctionDefinition);

            cosmosDbFunctionDefinition.IsDocumentCommand = documentCommandType.IsAssignableFrom(cosmosDbFunctionDefinition.CommandType);
            cosmosDbFunctionDefinition.IsDocumentBatchCommand = documentBatchCommandType.IsAssignableFrom(cosmosDbFunctionDefinition.CommandType);
            if (cosmosDbFunctionDefinition.IsDocumentCommand && cosmosDbFunctionDefinition.IsDocumentBatchCommand)
            {
                throw new ConfigurationException(
                    $"Command {cosmosDbFunctionDefinition.CommandType.Name} implements both ICosmosDbDocumentCommand and ICosmosDbDocumentBatchCommand - it can only implement one of these interfaces");
            }
        }

        private static void CompleteHttpFunctionDefinition(FunctionHostBuilder builder,
            HttpFunctionDefinition httpFunctionDefinition, AuthorizationBuilder authorizationBuilder,
            Type validationResultType)
        {
            if (!httpFunctionDefinition.Authorization.HasValue)
            {
                httpFunctionDefinition.Authorization = authorizationBuilder.AuthorizationDefaultValue;
            }

            if (httpFunctionDefinition.Authorization.Value == AuthorizationTypeEnum.TokenValidation)
            {
                httpFunctionDefinition.ValidatesToken = true;
            }

            if (httpFunctionDefinition.Verbs.Count == 0)
            {
                httpFunctionDefinition.Verbs.Add(HttpMethod.Get);
            }

            httpFunctionDefinition.ClaimsPrincipalAuthorizationType =
                httpFunctionDefinition.ClaimsPrincipalAuthorizationType ??
                httpFunctionDefinition.RouteConfiguration.ClaimsPrincipalAuthorizationType ??
                authorizationBuilder.DefaultClaimsPrincipalAuthorizationType;

            httpFunctionDefinition.HeaderBindingConfiguration =
                httpFunctionDefinition.HeaderBindingConfiguration ?? builder.DefaultHeaderBindingConfiguration;

            httpFunctionDefinition.HttpResponseHandlerType =
                httpFunctionDefinition.HttpResponseHandlerType ?? builder.DefaultHttpResponseHandlerType;

            httpFunctionDefinition.TokenHeader = httpFunctionDefinition.TokenHeader ?? authorizationBuilder.AuthorizationHeader ?? "Authorization";

            httpFunctionDefinition.IsValidationResult = httpFunctionDefinition.CommandResultType != null &&
                                                         validationResultType.IsAssignableFrom(httpFunctionDefinition
                                                             .CommandResultType);

            httpFunctionDefinition.IsStreamCommand =
                (typeof(IStreamCommand)).IsAssignableFrom(httpFunctionDefinition.CommandType); 

            httpFunctionDefinition.TokenValidatorType = httpFunctionDefinition.TokenValidatorType ?? authorizationBuilder.TokenValidatorType;

            if (httpFunctionDefinition.ValidatesToken && httpFunctionDefinition.TokenValidatorType == null)
            {
                throw new ConfigurationException($"Command {httpFunctionDefinition.CommandType.Name} expects to be authenticated with token validation but no token validator is registered");
            }

            HttpParameterExtractor hpe = new HttpParameterExtractor(httpFunctionDefinition);

            httpFunctionDefinition.RouteParameters = hpe.ExtractRouteParameters();

            httpFunctionDefinition.PossibleBindingProperties = hpe.ExtractPossibleQueryParameters();

            httpFunctionDefinition.PossibleFormProperties = hpe.ExtractPossibleFormParameters();

            EnsureOpenApiDescription(httpFunctionDefinition);
        }

        private static void EnsureOpenApiDescription(HttpFunctionDefinition httpFunctionDefinition)
        {
            if (httpFunctionDefinition.Route == null)
            {
                return;
            }
            // function definitions share route definitions so setting properties for one sets for all
            // but we set only if absent and based on the parent route
            // alternative would be to gather up the unique route configurations and set once but will
            // involve multiple loops
            Debug.Assert(httpFunctionDefinition.RouteConfiguration != null);
            if (string.IsNullOrWhiteSpace(httpFunctionDefinition.RouteConfiguration.OpenApiName))
            {
                string[] components = httpFunctionDefinition.RouteConfiguration.Route.Split('/');
                for (int index = components.Length - 1; index >= 0; index--)
                {
                    if (string.IsNullOrWhiteSpace(components[index]) || IsRouteParameter(components[index]))
                    {
                        continue;
                    }

                    httpFunctionDefinition.RouteConfiguration.OpenApiName = components[index];
                    break;
                }
            }
        }

        private static bool IsRouteParameter(string component)
        {
            return component.StartsWith("{") &&
                   component.EndsWith("}");
        }

        private static void ExtractCosmosCommandProperties(CosmosDbFunctionDefinition functionDefinition)
        {
            functionDefinition.CommandProperties = functionDefinition
                .CommandType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.GetCustomAttribute<SecurityPropertyAttribute>() == null
                            && x.SetMethod != null)
                .Select(x =>
                {
                    string cosmosPropertyName = x.Name;
                    JsonPropertyAttribute jsonPropertyAttribute = x.GetCustomAttribute<JsonPropertyAttribute>();
                    if (jsonPropertyAttribute != null)
                    {
                        cosmosPropertyName = jsonPropertyAttribute.PropertyName;
                    }
                    return new CosmosDbCommandProperty
                    {
                        Name = x.Name,
                        CosmosPropertyName = cosmosPropertyName,
                        TypeName = x.PropertyType.EvaluateType(),
                        Type = x.PropertyType
                    };
                })
                .ToArray();
        }
    }
}
