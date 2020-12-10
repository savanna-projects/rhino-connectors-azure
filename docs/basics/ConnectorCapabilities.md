[Home](../../README.md 'README') 

# Connector Capabilities
11/01/2020 - 10 minutes to read

## In This Article
* [Automation Provider Capabilities](#automation-provider-capabilities)  

Each connector implements it's own integration capabilities and the behavior depends on the implementation. Please follow this post in order to perform a successful integration with Azure.

## Automation Provider Capabilities
The list of optional capabilities which you can pass with Rhino Configuration.  

The options must be passed under `<connector_name>:options` key, as follow:

```js
...
"capabilities": {
  "connector_azure:options": {
    "testPlan": 1
    "areaPath": "Rhino Connector",
    "iterationPath": "Rhino Connector/Iteration 1"
    "customFields": {
      "System.CustomField": "Foo & Bar"
      ...
    }
    ...
  }
}
...
```  

|Name                |Type   |Description                                                                                                                     |
|--------------------|-------|--------------------------------------------------------------------------------------------------------------------------------|
|areaPath            |string |The area path under which items will be created (tests, bugs, etc.). If not selected, default value will be used.               |
|iterationPath       |string |The iteration under which items will be created (tests, bugs, etc.). If not selected, default will be picked up.                |
|customFields        |object |Key/Value pairs of custom fields to apply when creating items (tests, bugs, etc.). The fields names must be system fields names.|
|testPlan            |number |The test plan ID to use. If set, tests will be created under this plan. _**Mandatory for creating test runs on TFS <= 2017**_.  |
|testConfiguration   |number |The test configuration ID which will be used when running the current tests. If not selected, defaults values will be used.     |

## See Also
[Azure DevOps, Fields - List](https://docs.microsoft.com/en-us/rest/api/azure/devops/wit/fields/list?view=azure-devops-rest-5.1)
[Azure DevOps, Work Items Tracking API Documentation](https://docs.microsoft.com/en-us/rest/api/azure/devops/wit/?view=azure-devops-rest-5.1)