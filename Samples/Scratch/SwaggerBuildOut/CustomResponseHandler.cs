﻿using System;
using System.Threading.Tasks;
using AzureFromTheTrenches.Commanding.Abstractions;
using FunctionMonkey.Abstractions.Http;
using FunctionMonkey.Commanding.Abstractions.Validation;
using Microsoft.AspNetCore.Mvc;

namespace SwaggerBuildOut
{
    public class CustomResponseHandler : IHttpResponseHandler
    {
        public Task<IActionResult> CreateResponse<TCommand>(TCommand command, Exception ex) where TCommand : ICommand
        {
            return null;
        }

        public Task<IActionResult> CreateResponseFromException<TCommand>(TCommand command, Exception ex) where TCommand : ICommand
        {
            throw new NotImplementedException();
        }

        public Task<IActionResult> CreateResponse<TCommand, TResult>(TCommand command, TResult result) where TCommand : ICommand
        {
            if (result == null)
            {
                return Task.FromResult((IActionResult)new NoContentResult());
            }

            return null;
        }

        public Task<IActionResult> CreateResponse<TCommand>(TCommand command)
        {
            return null;
        }

        public Task<IActionResult> CreateValidationFailureResponse<TCommand>(TCommand command, ValidationResult validationResult) where TCommand : ICommand
        {
            return null;
        }

        public Task<IActionResult> CreateResponse<TCommand>(TCommand command, ValidationResult validationResult) where TCommand : ICommand
        {
            return null;
        }

        public Task<IActionResult> CreateResponse<TCommand, TResult>(TCommand command, ValidationResult<TResult> validationResult) where TCommand : ICommand<TResult>
        {
            return null;
        }
    }
}
