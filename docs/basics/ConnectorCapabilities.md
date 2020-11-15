[Home](../../README.md 'README') 

# Connector Capabilities
11/01/2020 - 10 minutes to read

## In This Article
* [Automation Provider Capabilities](#automation-provider-capabilites)  

Each connector implements it's own integration capabilities and the behavior depends on the implementation. Please follow this post in order to perform a successful integration with TestRail.

## Automation Provider Capabilities
The list of optional capabilities which you can pass with Rhino Configuration.  

The options must be passed under `<connector_name>:options` key, as follow:

```js
...
"capabilities": {
  "connector_azure:options": {
    "testPlan": 1
    "System.AreaPath": "Rhino Connector",
    "System.IterationPath": "Rhino Connector/Iteration 1"
    "customFields": {
      "System.CustomField": "Foo & Bar"
      ...
    }
    ...
  }
}
...
```  

System.IterationPath

|Name                |Type   |Description                                                                                                     |
|--------------------|-------|----------------------------------------------------------------------------------------------------------------|
|testPlan            |number |The test plan ID to use. If set, tests will be created under this plan (mandatory from TFS =< 2017).            |
|System.AreaPath     |string |The area path under which items will be created (tests, bugs, etc.). If not selected, default will be picked up.|
|System.IterationPath|string |The iteration under which items will be created (tests, bugs, etc.). If not selected, default will be picked up.|
|customFields        |object |Key/Value pairs of custom fields to apply when creating items (tests, bugs, etc.).                              |