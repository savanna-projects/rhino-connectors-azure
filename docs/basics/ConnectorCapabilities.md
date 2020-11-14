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
    ...
  }
}
...
```  

|Name          |Type   |Description                                                                    |
|--------------|-------|-------------------------------------------------------------------------------|
|testPlan      |number |The test plan ID to use. If set, all tests will be fetched from this plan only.|