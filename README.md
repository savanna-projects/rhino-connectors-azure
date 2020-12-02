# Gurock Connector - Overview
11/13/2020 - 5 minutes to read

## In This Article
* [Connector Capabilities](./docs/basics/ConnectorCapabilities.md 'ConnectorCapabilities')  

> This connector is using Azure REST API in order to integrate with Azure DevOps and TFS. Please note that some functionalities are not yet supported (on new versions) and some are not supported at all (old versions). The official support is for [Azure DevOps API v6.0](https://docs.microsoft.com/en-us/rest/api/azure/devops/) and above, but might work also on other versions within the REST API capabilities on that version.  

Rhino API connectors for using with [Azure DevOps or Team Foundation Server](https://azure.microsoft.com/en-us/services/devops/) product.

## Known Issues
* Team Foundation Server <= 2017 does not support REST API for getting test cases by _**Test Suite**_ or _**Test Plan**_. You can use _**Test Case ID**_ or query (ID or literal).
* Team Foundation Server <= 2017 does not support [TestPlanHttpClient](https://docs.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.services.testmanagement.testplanning.webapi.testplanhttpclient?view=azure-devops-dotnet-preview). The behavior is mitigated and replaced whenever possible, but some TestPlan/TestSuite functionalities will not be supported.
* Team Foundation Server <= 2017 does not support creating a Test Run without specifying a Test Plan ID. If your configuration is set to "dryRun: false", make sure to provide ["testPlan" capability](./ConnectorCapabilities.md).
* Team Foundation Server and Azure DevOps uses **@** to create data driven parameters. While some formats are automatically escaped (e.g. mail@mail.com) other will not (e.g.) ```//input[@name='q']``` will create a parameter called ```name``` and will break the XPath. A workaround for that case can be ```//input[@*='q']```.
* In order to remove redundancy with Team Foundation & Azure DevOps _*Shared Steps*_ entity, Azure connector is not supporting Rhino Plugins Engine. While it will work for executing tests and generating Rhino reports, it might report wrong results to Team Foundation/Azure DevOsp. If the support is required, please open an issue.

## See Also
* [Syntax for the Work Item Query Language (WIQL)](https://docs.microsoft.com/en-us/azure/devops/boards/queries/wiql-syntax?view=azure-devops)
* [Client Libraries](https://docs.microsoft.com/en-us/azure/devops/integrate/concepts/dotnet-client-libraries?view=azure-devops)
* [Azure DevOps Services REST API Reference](https://docs.microsoft.com/en-us/rest/api/azure/devops/)