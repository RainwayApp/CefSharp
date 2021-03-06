using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using CefSharp.ModelBinding.Converters;

namespace CefSharp.ModelBinding
{

    /// <summary>
    /// This class is responsible for marshaling Javascript objects into their corresponding .NET type. 
    /// </summary>
    /// <remarks>
    /// This binder has no backwards compatibility with the <see cref="DefaultBinder"/> due to changes in how data member are marshaled.
    /// </remarks>
    public class TypeSafeBinder : IBinder
    {
        /// <summary>
        /// Used to try and convert a generic type to an array via Reflection.
        /// </summary>
        private static readonly MethodInfo ToArrayMethodInfo = typeof(Enumerable).GetMethod("ToArray", BindingFlags.Public | BindingFlags.Static);

        /// <summary>
        /// Creates a new instance of the <see cref="TypeSafeBinder"/> and registers converters with <see cref="TypeDescriptor"/> <br/>
        /// This binder can reliably marshal data between the Javascript and .NET domains. <br/>
        /// For example it provides interoperability for Typescript, Javascript, and C# style conventions without making you drop conventions in one of your languages. <br/>
        /// It will also handle types like <see cref="Guid"/> or <see cref="Enum"/> fields that have flags. <br/>
        /// Finally it ensure there is type safety by throwing <see cref="TypeBindingException"/> whenever data is malformed so you can catch issues in your code.
        /// </summary>
        public TypeSafeBinder()
        {
            BinderGuidConverter.Register();
            BinderVersionConverter.Register();
        }

        /// <summary>
        /// Attempts to bind a Javascript object into a corresponding .NET type.
        /// </summary>
        /// <param name="javaScriptObject">An instance of the Javascript object.</param>
        /// <param name="nativeType">The type this method will try to create an instance of using the Javascript object.</param>
        /// <returns>An instance of the native type.</returns>
        public object Bind(object javaScriptObject, Type nativeType)
        {
            // if the intended destination is an enumeration, we can try and get the corresponding member upfront.
            // internally this will throw a TypeBindingException if it runs into issues. See the documentation for more information.
            if (nativeType.IsEnum)
            {
                return nativeType.CreateEnumMember(javaScriptObject);
            }

            // if the source object is null and is not an enum, then there isn't anything left to do.
            if (javaScriptObject == null)
            {
                return null;
            }
            // get the underlying type for the incoming object 
            var javaScriptType = javaScriptObject.GetType();

            // if the object can be directly assigned to the destination type, then return and let the runtime handle the rest.
            if (nativeType.IsAssignableFrom(javaScriptType))
            {
                return javaScriptObject;
            }

            // custom Type converters should be registered in the constructor before calling this
            var typeConverter = TypeDescriptor.GetConverter(javaScriptType);
            // If the object can be converted to the target (=> double to int, string to Guid), go for it.
            if (typeConverter.CanConvertTo(nativeType))
            {
                return typeConverter.ConvertTo(javaScriptObject, nativeType);
            }
            // collections have to be unwound 
            if (nativeType.IsCollection() || nativeType.IsArray() || nativeType.IsEnumerable())
            {
                return BindCollection(nativeType, javaScriptType, javaScriptObject);
            }
            return BindObject(nativeType, javaScriptType, javaScriptObject);
        }


        /// <summary>
        ///     Attempts to bind an object to a
        ///     <see cref="T:ValueTuple" /><br />
        ///     <see cref="T:ValueTuple{T1}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3, T4}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3, T4, T5}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3, T4, T5, T6}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3, T4, T5, T6, T7}" /><br />
        ///     <see cref="T:ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}" /><br />
        /// </summary>
        /// <param name="nativeType">
        ///     the generic <see cref="T:ValueTuple" /> the <paramref name="javaScriptObject" /> will be bound
        ///     to.
        /// </param>
        /// <param name="javaScriptObject">A collection that contains all the components of the tuple.</param>
        /// <returns>A tuple I'd fucking hope</returns>
        private object BindValueTuple(Type nativeType, object javaScriptObject)
        {
            if (!(javaScriptObject is IList<object> components))
            {
                return null;
            }

            // the zero index of the tuple
            const int index = 0;
            // all of the component types
            var types = (from field in nativeType.GetTypeInfo().DeclaredFields where field.IsPublic && !field.IsStatic select field.FieldType).ToArray();
            if (components.Count != types.Length)
            {
                throw new ArgumentOutOfRangeException($"The source object contains {components.Count} components. The number of component types found is {types.Length}.");
            }

            // The ValueTuple struct contains static methods for creating value tuples.
            // However generic type arguments aren't assigned which means the types technically are mismatched. 
            // (=> ValueTuple<object, object> is not the same as ValueTuple<SpecialObject, ManagedCode>.
            // So we use reflection to dynamically build the ValueTuple.Create call with our generic arguments.
            object Create(params object[] args)
            {
                return nativeType.GetMethods().First(m =>
                {
                    if (!string.Equals(m.Name, "Create", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    return m.GetParameters().Length == types.Length;
                }).MakeGenericMethod(types).Invoke(null, args);
            }

            // Who's That Pokémon?!
            // Using Activator.CreateInstance and a LINQ sort was less code,
            // but it throws an exception when not running .NET 4.7.2+
            switch ((components.Count - index))
            {
                case 0:
                    return null;
                case 1:
                    return Create(Bind(components[index], types[index]));
                case 2:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]));
                case 3:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]));
                case 4:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]));
                case 5:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]), Bind(components[index + 4], types[index + 4]));
                case 6:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]), Bind(components[index + 4], types[index + 4]), Bind(components[index + 5], types[index + 5]));
                case 7:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]), Bind(components[index + 4], types[index + 4]), Bind(components[index + 5], types[index + 5]), Bind(components[index + 6], types[index + 6]));
                case 8:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]), Bind(components[index + 4], types[index + 4]), Bind(components[index + 5], types[index + 5]), Bind(components[index + 6], types[index + 6]), Bind(components[index + 7], types[index + 7]));
                default:
                    return Create(Bind(components[index], types[index]), Bind(components[index + 1], types[index + 1]), Bind(components[index + 2], types[index + 2]), Bind(components[index + 3], types[index + 3]), Bind(components[index + 4], types[index + 4]), Bind(components[index + 5], types[index + 5]), Bind(components[index + 6], types[index + 6]), Bind(components.Skip(index + 6), components[index + 7].GetType()));
            }
        }


        /// <summary>
        /// Binds a Javascript collection to a .NET collection
        /// </summary>
        /// <param name="nativeType">The native collection type.</param>
        /// <param name="javaScriptType">The underlying type for the Javascript object.</param>
        /// <param name="javaScriptObject">The Javascript object that will be unwound to the native collection type.</param>
        /// <returns>
        /// A .NET collection which should have all the same value as the <paramref name="javaScriptObject"/>
        /// </returns>
        protected virtual object BindCollection(Type nativeType, Type javaScriptType, object javaScriptObject)
        {
            // if the Javascript object isn't a collection throw, we shouldn't have ended up here.
            if (!(javaScriptObject is ICollection javaScriptCollection))
            {
                throw new TypeBindingException(javaScriptObject.GetType(), nativeType, BindingFailureCode.SourceNotAssignable);
            }

            Type genericType;

            // check if the native type is a generic
            if (nativeType.GetTypeInfo().IsGenericType)
            {
                // get the generic backing type of the native type
                genericType = nativeType.GetGenericArguments().FirstOrDefault();
            }
            else
            {
                // otherwise it's a collection, and we get the backing type for that.
                var enumerable = nativeType.GetInterfaces().Where(i => i.GetTypeInfo().IsGenericType).FirstOrDefault(i => i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                genericType = enumerable?.GetGenericArguments()?.FirstOrDefault();
            }

            // if we don't have a generic type then just use object
            if (genericType == null)
            {
                genericType = typeof(object);
            }

            var modelType = typeof(List<>).MakeGenericType(genericType);
            var model = (IList)Activator.CreateInstance(modelType);

            // loop over the collection and assign the items to their corresponding types
            foreach (var javaScriptItem in javaScriptCollection)
            {
                // if the value is null then we'll add null to the collection,
                if (javaScriptItem == null)
                {
                    // for value types like int we'll create the default value and assign that as we cannot assign null
                    model.Add(genericType.IsValueType ? Activator.CreateInstance(genericType) : null);
                }
                else
                {
                    var valueType = javaScriptItem.GetType();
                    // if the collection item is a list or dictionary then we'll attempt to bind it
                    if (typeof(IDictionary<string, object>).IsAssignableFrom(valueType) || typeof(IList<object>).IsAssignableFrom(valueType))
                    {
                        var subModel = Bind(javaScriptItem, genericType);
                        model.Add(subModel);
                    }
                    else
                    {
                        model.Add(javaScriptItem);
                    }
                }
            }
            // if the native type is actually an array and not a list, then convert the mode above to an array
            if (nativeType.IsArray())
            {
                var genericToArrayMethod = ToArrayMethodInfo.MakeGenericMethod(genericType);
                return genericToArrayMethod.Invoke(null, new object[] { model });
            }
            // otherwise return the model
            return model;
        }

        /// <summary>
        /// Bind a Javascript object to a corresponding .NET type
        /// </summary>
        /// <param name="nativeType">The native type to bind against.</param>
        /// <param name="javaScriptType">The native type inferred from the Javascript object.</param>
        /// <param name="javaScriptObject">The Javascript object that will be bound.</param>
        /// <returns>
        /// An instance of the .NET type which should have all the same values as the <paramref name="javaScriptObject"/>
        /// </returns>
        protected virtual object BindObject(Type nativeType, Type javaScriptType, object javaScriptObject)
        {
            // create an instance of our native type. 
            // use nonPublic: true to signal we want public and non-public default constructors to activate
            var model = Activator.CreateInstance(nativeType, true);

            // If the object type is a dictionary then attempt to bind all the members of the Javascript object to their C# counterpart 
            if (typeof(IDictionary<string, object>).IsAssignableFrom(javaScriptType))
            {
                // all of the public and assignable Properties from the native Type
                var nativeMembers = BindingMemberInfo.CollectEncapsulatedProperties(nativeType).ToList();
                // loop over the Javascript object
                foreach (var javaScriptMember in (IDictionary<string, object>)javaScriptObject)
                {
                    // now for every Javascript member we try to find it's corresponding .NET member on the native Type 
                    foreach (var nativeMember in nativeMembers)
                    {
                        // make sure the native members name is an EXACT match to what would have been bound to the window
                        if (javaScriptMember.Key.Equals(nativeMember.ConvertNameToCamelCase()))
                        {
                            // bind the Javascript members value to to the native Type 
                            var nativeValue = Bind(javaScriptMember.Value, nativeMember.Type);
                            // and then set it on the instance of the native type we created
                            nativeMember.SetValue(model, nativeValue);
                        }
                    }
                    // if we failed to find a member on the .NET type whose name is equal to the Javascript member, throw.
                    // most likely the end-user is not using proper conventions on one side.
                    throw new TypeBindingException(javaScriptType, nativeType, BindingFailureCode.MemberNotFound, javaScriptMember.Key);
                }
            }
            return model;
        }

    }
}
