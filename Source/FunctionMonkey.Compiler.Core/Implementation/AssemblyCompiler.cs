﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using FunctionMonkey.Abstractions.Builders.Model;
using FunctionMonkey.Abstractions.Extensions;
using FunctionMonkey.Commanding.Abstractions;
using FunctionMonkey.Compiler.Core.HandlebarsHelpers;
using FunctionMonkey.SignalR;
using HandlebarsDotNet;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.ChangeFeedProcessor;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace FunctionMonkey.Compiler.Core.Implementation
{
    internal class AssemblyCompiler : IAssemblyCompiler
    {
        private readonly ICompilerLog _compilerLog;
        private readonly ITemplateProvider _templateProvider;
        
        public AssemblyCompiler(ICompilerLog compilerLog, ITemplateProvider templateProvider = null)
        {
            _compilerLog = compilerLog;
            _templateProvider = templateProvider ?? new TemplateProvider();
        }

        public bool Compile(IReadOnlyCollection<AbstractFunctionDefinition> functionDefinitions,
            Type backlinkType,
            PropertyInfo backlinkPropertyInfo,
            string newAssemblyNamespace,
            IReadOnlyCollection<string> externalAssemblyLocations,
            string outputBinaryFolder,
            string assemblyName,
            OpenApiOutputModel openApiOutputModel,
            CompileTargetEnum compileTarget,
            string outputAuthoredSourceFolder = null)
        {
            HandlebarsHelperRegistration.RegisterHelpers();
            IReadOnlyCollection<SyntaxTree> syntaxTrees = CompileSource(functionDefinitions,
                openApiOutputModel,
                backlinkType,
                backlinkPropertyInfo,
                newAssemblyNamespace,
                outputAuthoredSourceFolder);

            bool isFSharpProject = functionDefinitions.Any(x => x.IsFunctionalFunction);

            return CompileAssembly(
                syntaxTrees,
                externalAssemblyLocations,
                openApiOutputModel,
                outputBinaryFolder,
                assemblyName,
                newAssemblyNamespace,
                compileTarget,
                isFSharpProject);
        }

        private List<SyntaxTree> CompileSource(IReadOnlyCollection<AbstractFunctionDefinition> functionDefinitions,
            OpenApiOutputModel openApiOutputModel,
            Type backlinkType,
            PropertyInfo backlinkPropertyInfo,
            string newAssemblyNamespace,
            string outputAuthoredSourceFolder)
        {
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            DirectoryInfo directoryInfo =  outputAuthoredSourceFolder != null ? new DirectoryInfo(outputAuthoredSourceFolder) : null;
            if (directoryInfo != null && !directoryInfo.Exists)
            {
                directoryInfo = null;
            }
            foreach (AbstractFunctionDefinition functionDefinition in functionDefinitions)
            {
                string templateSource = _templateProvider.GetCSharpTemplate(functionDefinition);
                AddSyntaxTreeFromHandlebarsTemplate(templateSource, functionDefinition.Name, functionDefinition, directoryInfo, syntaxTrees);
            }

            if (openApiOutputModel != null && openApiOutputModel.IsConfiguredForUserInterface)
            {
                string templateSource = _templateProvider.GetTemplate("swaggerui","csharp");
                AddSyntaxTreeFromHandlebarsTemplate(templateSource, "SwaggerUi", new
                {
                    Namespace = newAssemblyNamespace
                }, directoryInfo, syntaxTrees);
            }

            {
                string templateSource = _templateProvider.GetTemplate("startup", "csharp");
                AddSyntaxTreeFromHandlebarsTemplate(templateSource, "Startup", new
                {
                    Namespace = newAssemblyNamespace
                }, directoryInfo, syntaxTrees);
            }

            CreateLinkBack(functionDefinitions, backlinkType, backlinkPropertyInfo, newAssemblyNamespace, directoryInfo, syntaxTrees);

            return syntaxTrees;
        }

        private void CreateLinkBack(
            IReadOnlyCollection<AbstractFunctionDefinition> functionDefinitions,
            Type backlinkType,
            PropertyInfo backlinkPropertyInfo,
            string newAssemblyNamespace,
            DirectoryInfo directoryInfo,
            List<SyntaxTree> syntaxTrees)
        {
            if (backlinkType == null) return; // back link referencing has been disabled
            
            // Now we need to create a class that references the assembly with the configuration builder
            // otherwise the reference will be optimised away by Roslyn and it will then never get loaded
            // by the function host - and so at runtime the builder with the runtime info in won't be located
            string linkBackTemplateSource = _templateProvider.GetCSharpLinkBackTemplate();
            Func<object, string> linkBackTemplate = Handlebars.Compile(linkBackTemplateSource);

            LinkBackModel linkBackModel = null;
            if (backlinkPropertyInfo != null)
            {
                linkBackModel = new LinkBackModel
                {
                    TypeName = backlinkType.EvaluateType(),
                    PropertyName = backlinkPropertyInfo.Name,
                    PropertyTypeName = backlinkPropertyInfo.PropertyType.EvaluateType(),
                    Namespace = newAssemblyNamespace
                };
            }
            else
            {
                linkBackModel = new LinkBackModel
                {
                    TypeName = backlinkType.EvaluateType(),
                    PropertyName = null,
                    Namespace = newAssemblyNamespace
                };
            }
            
            string outputLinkBackCode = linkBackTemplate(linkBackModel);
            OutputDiagnosticCode(directoryInfo, "ReferenceLinkBack", outputLinkBackCode);
            SyntaxTree linkBackSyntaxTree = CSharpSyntaxTree.ParseText(outputLinkBackCode);
            syntaxTrees.Add(linkBackSyntaxTree);
        }

        private static void AddSyntaxTreeFromHandlebarsTemplate(string templateSource, string name,
            object functionDefinition, DirectoryInfo directoryInfo, List<SyntaxTree> syntaxTrees)
        {
            Func<object, string> template = Handlebars.Compile(templateSource);

            string outputCode = template(functionDefinition);
            OutputDiagnosticCode(directoryInfo, name, outputCode);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(outputCode, path:$"{name}.cs");
            //syntaxTree = syntaxTree.WithFilePath($"{name}.cs");
            syntaxTrees.Add(syntaxTree);
        }

        private static void OutputDiagnosticCode(DirectoryInfo directoryInfo, string name,
            string outputCode)
        {
            if (directoryInfo != null)
            {
                using (StreamWriter writer =
                    File.CreateText(Path.Combine(directoryInfo.FullName, $"{name}.cs")))
                {
                    writer.Write(outputCode);
                }
            }
        }

        private bool CompileAssembly(IReadOnlyCollection<SyntaxTree> syntaxTrees,
            IReadOnlyCollection<string> externalAssemblyLocations,
            OpenApiOutputModel openApiOutputModel,
            string outputBinaryFolder,
            string outputAssemblyName,
            string assemblyNamespace,
            CompileTargetEnum compileTarget,
            bool isFSharpProject)
        {
            IReadOnlyCollection<string> locations = BuildCandidateReferenceList(externalAssemblyLocations, compileTarget, isFSharpProject);
            const string manifestResourcePrefix = "FunctionMonkey.Compiler.references.netstandard2._0.";
            // For each assembly we've found we need to check and see if it is already included in the output binary folder
            // If it is then its referenced already by the function host and so we add a reference to that version.
            List<string> resolvedLocations = ResolveLocationsWithExistingReferences(outputBinaryFolder, locations);

            string[] manifestResoureNames = GetType().Assembly.GetManifestResourceNames()
                .Where(x => x.StartsWith(manifestResourcePrefix))
                .Select(x => x.Substring(manifestResourcePrefix.Length))
                .ToArray();

            List<PortableExecutableReference> references = BuildReferenceSet(resolvedLocations, manifestResoureNames, manifestResourcePrefix, compileTarget);
            
            CSharpCompilation compilation = CSharpCompilation.Create(assemblyNamespace) //(outputAssemblyName)
                    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                    .AddReferences(references)
                    .AddSyntaxTrees(syntaxTrees)
                ;

            List<ResourceDescription> resources = null;
            if (openApiOutputModel != null)
            {
                resources = new List<ResourceDescription>();
                Debug.Assert(openApiOutputModel.OpenApiSpecification != null);
                resources.Add(new ResourceDescription($"{assemblyNamespace}.OpenApi.{openApiOutputModel.OpenApiSpecification.Filename}",
                    () => new MemoryStream(Encoding.UTF8.GetBytes(openApiOutputModel.OpenApiSpecification.Content)), true));
                if (openApiOutputModel.SwaggerUserInterface != null)
                {
                    foreach (OpenApiFileReference fileReference in openApiOutputModel.SwaggerUserInterface)
                    {
                        OpenApiFileReference closureCapturedFileReference = fileReference;
                        resources.Add(new ResourceDescription($"{assemblyNamespace}.OpenApi.{closureCapturedFileReference.Filename}",
                            () => new MemoryStream(Encoding.UTF8.GetBytes(closureCapturedFileReference.Content)), true));
                    }
                }
            }

            string outputFilename = Path.Combine(outputBinaryFolder, outputAssemblyName);
            EmitResult compilationResult = compilation.Emit(outputFilename, manifestResources: resources);
            if (!compilationResult.Success)
            {
                IEnumerable<Diagnostic> failures = compilationResult.Diagnostics.Where(diagnostic =>
                    diagnostic.IsWarningAsError ||
                    diagnostic.Severity == DiagnosticSeverity.Error);
                    
                foreach (Diagnostic diagnostic in failures)
                {
                    _compilerLog.Error($"Error compiling function: {diagnostic.ToString()}");
                }
            }

            return compilationResult.Success;
        }

        private List<PortableExecutableReference> BuildReferenceSet(List<string> resolvedLocations,
            string[] manifestResoureNames,
            string manifestResourcePrefix,
            CompileTargetEnum compileTarget)
        {
            // Add our references - if the reference is to a library that forms part of NET Standard 2.0 then make sure we add
            // the reference from the embedded NET Standard reference set - although our target is NET Standard the assemblies
            // in the output folder of the Function App may be NET Core assemblies.
            List<PortableExecutableReference> references = resolvedLocations.Select(x =>
            {
                if (compileTarget == CompileTargetEnum.NETStandard20)
                {
                    string assemblyFilename = Path.GetFileName(x);
                    string manifestResourceName =
                        manifestResoureNames.SingleOrDefault(m =>
                            String.Equals(assemblyFilename, m, StringComparison.CurrentCultureIgnoreCase));
                    if (manifestResourceName != null)
                    {
                        using (Stream lib = GetType().Assembly
                            .GetManifestResourceStream(String.Concat(manifestResourcePrefix, manifestResourceName)))
                        {
                            return MetadataReference.CreateFromStream(lib);
                        }
                    }
                }

                return MetadataReference.CreateFromFile(x);

            }).ToList();

            if (compileTarget == CompileTargetEnum.NETStandard20)
            {
                using (Stream netStandard = GetType().Assembly
                    .GetManifestResourceStream("FunctionMonkey.Compiler.Core.references.netstandard2._0.netstandard.dll"))
                {
                    references.Add(MetadataReference.CreateFromStream(netStandard));
                }

                using (Stream netStandard = GetType().Assembly
                    .GetManifestResourceStream("FunctionMonkey.Compiler.Core.references.netstandard2._0.System.Runtime.dll"))
                {
                    references.Add(MetadataReference.CreateFromStream(netStandard));
                }

                using (Stream systemIo = GetType().Assembly
                    .GetManifestResourceStream(String.Concat(manifestResourcePrefix, "System.IO.dll")))
                {
                    references.Add(MetadataReference.CreateFromStream(systemIo));
                }
            }

            return references;
        }

        private static IReadOnlyCollection<string> BuildCandidateReferenceList(
            IReadOnlyCollection<string> externalAssemblyLocations,
            CompileTargetEnum compileTarget,
            bool isFSharpProject)
        {
            // These are assemblies that Roslyn requires from usage within the template
            HashSet<string> locations = new HashSet<string>
            {
                typeof(Task).GetTypeInfo().Assembly.Location,
                typeof(Runtime).GetTypeInfo().Assembly.Location,
                typeof(IStreamCommand).Assembly.Location,
                typeof(AzureFromTheTrenches.Commanding.Abstractions.ICommand).GetTypeInfo().Assembly.Location,
                typeof(Abstractions.ISerializer).GetTypeInfo().Assembly.Location,
                typeof(System.Net.Http.HttpMethod).GetTypeInfo().Assembly.Location,
                typeof(System.Net.HttpStatusCode).GetTypeInfo().Assembly.Location,
                typeof(HttpRequest).Assembly.Location,
                typeof(JsonConvert).GetTypeInfo().Assembly.Location,
                typeof(OkObjectResult).GetTypeInfo().Assembly.Location,
                typeof(IActionResult).GetTypeInfo().Assembly.Location,
                typeof(FunctionNameAttribute).GetTypeInfo().Assembly.Location,
                typeof(ILogger).GetTypeInfo().Assembly.Location,
                typeof(IServiceProvider).GetTypeInfo().Assembly.Location,
                typeof(IHeaderDictionary).GetTypeInfo().Assembly.Location,
                typeof(StringValues).GetTypeInfo().Assembly.Location,
                typeof(ExecutionContext).GetTypeInfo().Assembly.Location,
                typeof(Document).GetTypeInfo().Assembly.Location,
                typeof(Message).GetTypeInfo().Assembly.Location,
                typeof(ChangeFeedProcessorBuilder).Assembly.Location,
                typeof(CosmosDBAttribute).Assembly.Location,
                typeof(TimerInfo).Assembly.Location,
                typeof(DbConnectionStringBuilder).Assembly.Location,
                typeof(AzureSignalRAuthClient).Assembly.Location,
                typeof(System.Environment).Assembly.Location,
                typeof(HttpTriggerAttribute).Assembly.Location,
                typeof(ServiceBusAttribute).Assembly.Location,
                typeof(QueueAttribute).Assembly.Location,
                typeof(Microsoft.IdentityModel.Protocols.HttpDocumentRetriever).Assembly.Location,
                typeof(IServiceCollection).Assembly.Location
            };

            if (isFSharpProject)
            {
                locations.Add(typeof(FSharpOption<>).Assembly.Location);
            }

            
            if (compileTarget == CompileTargetEnum.NETCore21)
            {
                // we're a 3.x assembly so we can use our assemblies
                Assembly[] currentAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                locations.Add(currentAssemblies.Single(x => x.GetName().Name == "netstandard").Location);
                locations.Add(currentAssemblies.Single(x => x.GetName().Name == "System.Runtime").Location); // System.Runtime
                locations.Add(typeof(TargetFrameworkAttribute).Assembly.Location); // NetCoreLib
                locations.Add(typeof(System.Linq.Enumerable).Assembly.Location); // System.Linq
                locations.Add(typeof(System.Security.Claims.ClaimsPrincipal).Assembly.Location);
                locations.Add(typeof(System.Uri).Assembly.Location);
                locations.Add(currentAssemblies.Single(x => x.GetName().Name == "System.Collections").Location);
                locations.Add(currentAssemblies.Single(x => x.GetName().Name == "System.Threading").Location);
                locations.Add(currentAssemblies.Single(x => x.GetName().Name == "System.Threading.Tasks").Location);
            }
            

            foreach (string externalAssemblyLocation in externalAssemblyLocations)
            {
                locations.Add(externalAssemblyLocation);
            }

            return locations;
        }

        private static List<string> ResolveLocationsWithExistingReferences(string outputBinaryFolder, IReadOnlyCollection<string> locations)
        {
            List<string> resolvedLocations = new List<string>(locations.Count);
            foreach (string location in locations)
            {
                // if the reference is already in the location
                if (Path.GetDirectoryName(location) == outputBinaryFolder)
                {
                    resolvedLocations.Add(location);
                    continue;
                }

                // if the assembly we've picked up from the compiler bundle is in the output folder then we use the one in
                // the output folder
                string pathInOutputFolder = Path.Combine(outputBinaryFolder, Path.GetFileName(location));
                if (File.Exists(pathInOutputFolder))
                {
                    resolvedLocations.Add(location);
                    continue;
                }

                resolvedLocations.Add(location);
            }

            return resolvedLocations;
        }
    }
}
