﻿using FunctionMonkey.Abstractions.Builders;
using FunctionMonkey.Model;

namespace FunctionMonkey.Builders
{
    internal class OpenApiBuilder : IOpenApiBuilder
    {
        private readonly OpenApiConfiguration _openApiConfiguration;

        public OpenApiBuilder(OpenApiConfiguration openApiConfiguration)
        {
            _openApiConfiguration = openApiConfiguration;
        }


        public IOpenApiBuilder Version(string version)
        {
            _openApiConfiguration.Version = version;
            return this;
        }

        public IOpenApiBuilder Title(string title)
        {
            _openApiConfiguration.Title = title;
            return this;
        }

        public IOpenApiBuilder ApiSpecVersion(ApiSpecVersion specVersion)
        {
            _openApiConfiguration.ApiSpecVersion = specVersion;
            return this;
        }

        public IOpenApiBuilder ApiOutputFormat(ApiOutputFormat outputFormat)
        {
            _openApiConfiguration.ApiOutputFormat = outputFormat;
            return this;
        }

        public IOpenApiBuilder Servers(params string[] urls)
        {
            _openApiConfiguration.Servers = urls;
            return this;
        }

        public IOpenApiBuilder UserInterface(string route = "/swagger")
        {
            _openApiConfiguration.UserInterfaceRoute = route;
            return this;
        }
    }
}
