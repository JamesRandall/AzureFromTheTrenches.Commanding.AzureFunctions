namespace FunctionMonkey.FSharp
open System
open System.Net.Http
open System.Reflection
open System.Text.RegularExpressions
open FunctionMonkey.Abstractions
open FunctionMonkey.Abstractions
open FunctionMonkey.Abstractions
open FunctionMonkey.Abstractions.Builders
open FunctionMonkey.Abstractions.Builders.Model
open FunctionMonkey.Abstractions.Extensions
open FunctionMonkey.Abstractions.Http
open FunctionMonkey.Commanding.Abstractions.Validation
open FunctionMonkey.Model
open FunctionMonkey.Model
open Models

module FunctionCompilerMetadata =
    
    let private (|Match|_|) pattern input =
        let m = Regex.Match(input, pattern) in
        if m.Success then Some (List.tail [ for g in m.Groups -> g.Value ]) else None
    
    let create configuration =
        let createHttpFunctionDefinition (configuration:FunctionAppConfiguration) httpFunction =
            let convertVerb verb =
                match verb with
                | Get -> HttpMethod.Get
                | Put -> HttpMethod.Put
                | Post -> HttpMethod.Post
                | Patch -> HttpMethod.Patch
                | Delete -> HttpMethod.Delete
                
            let extractConstructorParameters () =
                let createParameter (cp:ParameterInfo) =
                    ImmutableTypeConstructorParameter(
                        Name = cp.Name,
                        Type = cp.ParameterType
                    )                    
                httpFunction.commandType.GetConstructors().[0].GetParameters() |> Seq.map createParameter |> Seq.toList
                
            let extractRouteParameters () =
                let createRouteParameter (parameterName:string) =
                    let isOptional = parameterName.EndsWith("?")
                    let parts = parameterName.Split(':')
                    let routeParameterName = parts.[0].TrimEnd('?')
                    
                    let matchedProperty = httpFunction.commandType.GetProperties() |> Seq.find (fun p -> p.Name.ToLower() = routeParameterName.ToLower())
                    let isPropertyNullable = matchedProperty.PropertyType.IsValueType && not (Nullable.GetUnderlyingType(matchedProperty.PropertyType) = null)
                    
                    let routeTypeName = match isOptional && isPropertyNullable with
                                        | true -> sprintf "{%s}?" (matchedProperty.PropertyType.EvaluateType())
                                        | false -> matchedProperty.PropertyType.EvaluateType()
                    
                    HttpParameter(
                                     Name = matchedProperty.Name,
                                     Type = matchedProperty.PropertyType,
                                     IsOptional = isOptional,
                                     IsNullableType = not (Nullable.GetUnderlyingType(matchedProperty.PropertyType) = null),
                                     RouteName = routeParameterName,
                                     RouteTypeName = routeTypeName
                                 )
                    
                match httpFunction.route with
                | Match "{(.*?)}" routeParams -> routeParams |> Seq.map createRouteParameter |> Seq.toList
                | _ -> []
                
                
            let httpFunctionDefinition =
                let constructorParameters = extractConstructorParameters ()
                HttpFunctionDefinition(
                    httpFunction.commandType,
                    httpFunction.resultType,
                    UsesImmutableTypes = true,
                    Verbs = System.Collections.Generic.HashSet(httpFunction.verbs |> Seq.map convertVerb),
                    Authorization = new System.Nullable<AuthorizationTypeEnum>(configuration.authorization.defaultAuthorizationMode),
                    ValidatesToken = (configuration.authorization.defaultAuthorizationMode = AuthorizationTypeEnum.TokenValidation),
                    TokenHeader = configuration.authorization.defaultAuthorizationHeader,
                    ClaimsPrincipalAuthorizationType = null,
                    HeaderBindingConfiguration = null,
                    HttpResponseHandlerType = null,
                    IsValidationResult = (not (httpFunction.resultType = typedefof<unit>) && typedefof<ValidationResult>.IsAssignableFrom(httpFunction.resultType)),
                    IsStreamCommand = false,
                    TokenValidatorType = null,
                    RouteParameters = extractRouteParameters (),
                    ImmutableTypeConstructorParameters = constructorParameters,
                    FunctionHandler = httpFunction.handler
                )
            
            httpFunctionDefinition :> AbstractFunctionDefinition
        
        {
            outputAuthoredSourceFolder = configuration.diagnostics.outputSourcePath
            openApiConfiguration = OpenApiConfiguration()
            functionDefinitions =
                [] |> 
                Seq.append (configuration.functions.httpFunctions |> Seq.map (fun f -> createHttpFunctionDefinition configuration f))
                |> Seq.toList
                
        } :> IFunctionCompilerMetadata
