namespace FunctionMonkey.FSharp

open System
open System.Linq.Expressions
open FunctionMonkey.Model.OutputBindings
open FunctionMonkey.Model.OutputBindings
open Models

module OutputBindings =
    let cosmosDb collectionName databaseName (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        outputBindingTarget.setOutputBinding(
            new CosmosOutputBinding(
               FunctionMonkey.Extensions.Utils.EvaluateType(outputBindingTarget.resultType),
               String.Empty,
               DatabaseName = databaseName,
               CollectionName = collectionName
            )
        )
        
    let withCosmosDbConnectionStringSettingName connectionStringSettingName (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        let outputBinding = outputBindingTarget.getOutputBinding()
        Option.bind (fun (binding:obj) ->
            let serviceBusBinding = binding :?> CosmosOutputBinding
            serviceBusBinding.ConnectionStringSettingName <- connectionStringSettingName
            Some binding
        ) outputBinding |> ignore
        outputBindingTarget.getFunction()

    let serviceBusQueue queueName (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        outputBindingTarget.setOutputBinding(
            new ServiceBusQueueOutputBinding(
                FunctionMonkey.Extensions.Utils.EvaluateType(outputBindingTarget.resultType),
                String.Empty,
                QueueName = queueName
            )
        )
        
    let withServiceBusSessionIdProperty (sessionIdProperty: Expression<Func<'commandType, 'propertyType>>) (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        let outputBinding = outputBindingTarget.getOutputBinding()
        Option.bind (fun (binding:obj) ->
            let serviceBusBinding = binding :?> ServiceBusQueueOutputBinding
            serviceBusBinding.SessionIdPropertyName <- (InternalHelpers.getPropertyInfo sessionIdProperty).Name
            Some binding
        ) outputBinding |> ignore
        outputBindingTarget.getFunction()
        
    let storageTable tableName (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        outputBindingTarget.setOutputBinding(
            new StorageTableOutputBinding(
                 FunctionMonkey.Extensions.Utils.EvaluateType(outputBindingTarget.resultType),
                 String.Empty,
                 TableName = tableName
            )
        )
    
    // The below apply to all bindings    
    let withConnectionStringSettingName connectionStringSettingName (outputBindingTarget:IOutputBindingTarget<'functionType>) =
        let outputBinding = outputBindingTarget.getOutputBinding()
        Option.bind (fun (binding:obj) ->
            let connectionStringBinding = binding :?> AbstractConnectionStringOutputBinding
            connectionStringBinding.ConnectionStringSettingName <- connectionStringSettingName
            Some binding
        ) outputBinding |> ignore
        outputBindingTarget.getFunction()
        
