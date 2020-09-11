﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using FunctionMonkey.Abstractions.Builders;
using FunctionMonkey.Abstractions.Http;
using FunctionMonkey.Compiler.Core.Extensions;

namespace FunctionMonkey.Compiler.Core.Implementation.OpenApi
{
    public class OpenApiCompilerConfiguration
    {
        public IList<IOpenApiDocumentFilter> DocumentFilters { get; } = new List<IOpenApiDocumentFilter>();

        public IList<IOpenApiDocumentFilter> ReDocDocumentFilters { get; } = new List<IOpenApiDocumentFilter>();

        public IList<IOpenApiOperationFilter> OperationFilters { get; } = new List<IOpenApiOperationFilter>();

        public IList<IOpenApiParameterFilter> ParameterFilters { get; } = new List<IOpenApiParameterFilter>();

        public IList<IOpenApiSchemaFilter> SchemaFilters { get; } = new List<IOpenApiSchemaFilter>();

        public Func<Type, string> SchemaIdSelector { get; set; }

        public OpenApiCompilerConfiguration(OpenApiConfiguration configuration)
        {
            foreach (var documentFilterFactory in configuration.DocumentFilterFactories)
            {
                DocumentFilters.Add(documentFilterFactory());
            }

            foreach (var reDocDocumentFilterFactory in configuration.ReDocDocumentFilterFactories)
            {
                ReDocDocumentFilters.Add(reDocDocumentFilterFactory());
            }

            foreach (var injectedExtensions in configuration.InjectedExtensions)
            {
                DocumentFilters.Add(new OpenApiExtensionInjectingDocumentFilter(injectedExtensions.resourceAssembly, injectedExtensions.resourceName, injectedExtensions.documentRoute));
            }

            foreach (var reDocInjectedExtensions in configuration.ReDocInjectedExtensions)
            {
                ReDocDocumentFilters.Add(new OpenApiExtensionInjectingDocumentFilter(reDocInjectedExtensions.resourceAssembly, reDocInjectedExtensions.resourceName, reDocInjectedExtensions.documentRoute));
            }

            foreach (var injectedTags in configuration.InjectedTags)
            {
                DocumentFilters.Add(new OpenApiTagInjectingDocumentFilter(injectedTags.resourceAssembly, injectedTags.resourceName, injectedTags.documentRoute));
            }

            foreach (var reDocInjectedTags in configuration.ReDocInjectedTags)
            {
                ReDocDocumentFilters.Add(new OpenApiTagInjectingDocumentFilter(reDocInjectedTags.resourceAssembly, reDocInjectedTags.resourceName, reDocInjectedTags.documentRoute));
            }

            foreach (var operationFilterFactory in configuration.OperationFilterFactories)
            {
                OperationFilters.Add(operationFilterFactory());
            }

            foreach (var parameterFilterFactory in configuration.ParameterFilterFactories)
            {
                ParameterFilters.Add(parameterFilterFactory());
            }

            foreach (var schemaFilterFactory in configuration.SchemaFilterFactories)
            {
                SchemaFilters.Add(schemaFilterFactory());
            }

            foreach (var xmlDocFactory in configuration.XmlDocFactories)
            {
                SchemaFilters.Add(new OpenApiXmlCommentsSchemaFilter(xmlDocFactory()));
            }

            SchemaIdSelector = configuration.SchemaIdSelector;
            if (SchemaIdSelector == null)
            {
                SchemaIdSelector = type =>
                {
                    var typeName = type.GetAttributeValue((DataContractAttribute attribute) => attribute.Name) ?? type.FriendlyId();
                    return typeName.SanitizeClassName();
                };
            }
        }
    }
}
