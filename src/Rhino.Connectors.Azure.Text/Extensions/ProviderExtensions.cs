using Rhino.Api.Interfaces;

using System.Reflection;

namespace Rhino.Connectors.Azure.Text.Extensions
{
    internal static class ProviderExtensions
    {
        public static T InvokeMethod<T>(this IProviderManager provider, string method, object[] parameters)
        {
            // constants
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // setup
            var onMethod = provider.GetType().GetMethod(method, Flags);

            // not found
            if (onMethod == null)
            {
                return default;
            }

            // invoke
            return (T)onMethod.Invoke(provider, parameters);
        }

        public static void InvokeMethod(this IProviderManager provider, string method, object[] parameters)
        {
            // constants
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            // setup
            var onMethod = provider.GetType().GetMethod(method, Flags);

            // not found
            if (onMethod == null)
            {
                return;
            }

            // invoke
            onMethod.Invoke(provider, parameters);
        }
    }
}
