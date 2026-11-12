using System;
using System.Collections.Generic;

namespace OrderDesk.Api.Infrastructure
{
    /// <summary>
    /// Hand-rolled service locator. Controllers reach into it from their default constructors,
    /// so their dependencies are invisible from the outside.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, Func<object>> Registrations = new Dictionary<Type, Func<object>>();

        public static void Register<T>(Func<T> factory) where T : class
        {
            Registrations[typeof(T)] = () => factory();
        }

        public static T Resolve<T>() where T : class
        {
            return (T)Registrations[typeof(T)]();
        }
    }
}
