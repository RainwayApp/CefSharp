// Copyright © 2016 The CefSharp Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style license that can be found in the LICENSE file.

using CefSharp.ModelBinding;

namespace CefSharp
{
    /// <summary>
    /// Javascript binding options
    /// </summary>
    public class BindingOptions
    {
        /// <summary>
        /// Returns the default binder based on the current <see cref="CefSharpSettings"/> that are defined
        /// </summary>
        /// <returns>An instance of the default binder.</returns>
        private static IBinder GetDefaultBinder()
        {
            if (CefSharpSettings.TypeSafeMarshaling)
            {
                return new TypeSafeBinder();
            }
            return new DefaultBinder();
        }

        /// <summary>
        /// Set of options with the default binding
        /// </summary>
        public static BindingOptions DefaultBinder
        {
            get { return new BindingOptions { Binder = GetDefaultBinder() }; }
        }

        /// <summary>
        /// Camel case the javascript names of properties/methods, defaults to true
        /// </summary>
        public bool CamelCaseJavascriptNames { get; set; }


        /// <summary>
        /// Model binder used for passing complex classes as params to methods
        /// </summary>
        public IBinder Binder { get; set; }

        /// <summary>
        /// Interceptor used for intercepting calls to the target object methods. For instance, can be used 
        /// for logging calls (from js) to .net methods.
        /// </summary>
        public IMethodInterceptor MethodInterceptor { get; set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public BindingOptions()
        {
            CamelCaseJavascriptNames = true;
        }
    }
}
