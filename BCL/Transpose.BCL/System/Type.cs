using System.ComponentModel;
using System.Reflection;

namespace System
{
    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public abstract class Type
    {
        public extern string FullName
        {
            [Transpose.Template("Transpose.Reflection.getTypeFullName({this})")]
            get;
        }

        /// <summary>
        /// Gets the type from which the current Type directly inherits.
        /// </summary>
        public extern Type BaseType
        {
            [Transpose.Template("Transpose.Reflection.getBaseType({this})")]
            get;
        }

        [Transpose.Template("Transpose.Reflection.isAssignableFrom({this}, {type})")]
        public extern bool IsAssignableFrom(Type type);

        public extern string AssemblyQualifiedName
        {
            [Transpose.Template("Transpose.Reflection.getTypeQName({this})")]
            get;
        }

        public extern string Name
        {
            [Transpose.Template("Transpose.Reflection.getTypeName({this})")]
            get;
        }

        public extern string Namespace
        {
            [Transpose.Template("Transpose.Reflection.getTypeNamespace({this})")]
            get;
        }

        public extern Assembly Assembly
        {
            [Transpose.Template("Transpose.Reflection.getTypeAssembly({this})")]
            get;
        }

        [Transpose.Template("Transpose.Reflection.getType({typeName})")]
        public static extern Type GetType(string typeName);

        [Transpose.Template("{this}.apply(null, {typeArguments})")]
        public extern Type MakeGenericType(Type[] typeArguments);

        [Transpose.Template("Transpose.Reflection.getGenericTypeDefinition({this})")]
        public extern Type GetGenericTypeDefinition();

        public extern bool IsGenericTypeDefinition
        {
            [Transpose.Template("Transpose.Reflection.isGenericTypeDefinition({this})")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the current type is a generic type.
        /// </summary>
        public extern bool IsGenericType
        {
            [Transpose.Template("Transpose.Reflection.isGenericType({this})")]
            get;
        }

        public extern int GenericParameterCount
        {
            [Transpose.Template("Transpose.Reflection.getGenericParameterCount({this})")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is abstract and must be overridden.
        /// </summary>
        public extern bool IsAbstract
        {
            [Transpose.Template("Transpose.Reflection.isAbstract({this})")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is declared sealed.
        /// </summary>
        public extern bool IsSealed
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 256)  != 0)")]
            get;
        }

        /// <summary>
        /// Gets the type that declares the current nested type or generic type parameter.
        /// </summary>
        public extern Type DeclaringType
        {
            [Transpose.Template("Transpose.Reflection.getMetaValue({this}, \"td\", null)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the current Type object represents a type whose definition is nested inside the definition of another type.
        /// </summary>
        public extern bool IsNested
        {
            [Transpose.Template("(Transpose.Reflection.getMetaValue({this}, \"td\", null) != null)")]
            get;
        }

        /// <summary>
        /// Gets the attributes associated with the Type.
        /// </summary>
        public extern TypeAttributes Attributes
        {
            [Transpose.Template("Transpose.Reflection.getMetaValue({this}, \"att\", 0)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the current Type object has type parameters that have not been replaced by specific types.
        /// </summary>
        public extern bool ContainsGenericParameters
        {
            [Transpose.Template("Transpose.Reflection.containsGenericParameters({this})")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the current Type represents a type parameter in the definition of a generic type or method.
        /// </summary>
        public extern bool IsGenericParameter
        {
            [Transpose.Template("({this}.$isTypeParameter || false)")]
            get;
        }

        /// <summary>
        /// Gets the position of the type parameter in the type parameter list of the generic type or method that declared the parameter, when the Type object represents a type parameter of a generic type or a generic method.
        /// </summary>
        public extern int GenericParameterPosition
        {
            [Transpose.Template("Transpose.Reflection.genericParameterPosition({this})")]
            get;
        }

        public extern MethodInfo DeclaringMethod
        {
            [Transpose.Template("Transpose.Reflection.getMetaValue({this}, \"md\", null)")]
            get;
        }

        /// <summary>
        /// Returns an array of Type objects that represent the type arguments of a closed generic type or the type parameters of a generic type definition.
        /// </summary>
        /// <returns>An array of Type objects that represent the type arguments of a generic type. Returns an empty array if the current type is not a generic type.</returns>
        [Transpose.Template("Transpose.Reflection.getGenericArguments({this})")]
        public extern Type[] GetGenericArguments();

        [Transpose.Template("Transpose.Reflection.getInterfaces({this})")]
        public extern Type[] GetInterfaces();

        [Transpose.Template("({this}.prototype instanceof {type})")]
        public extern bool IsSubclassOf(Type type);

        public extern bool IsClass
        {
            [Transpose.Template("Transpose.Reflection.isClass({this})")]
            get;
        }

        public extern bool IsEnum
        {
            [Transpose.Template("Transpose.Reflection.isEnum({this})")]
            get;
        }

        public extern bool IsFlags
        {
            [Transpose.Template("Transpose.Reflection.isFlags({this})")]
            get;
        }

        public extern bool IsInterface
        {
            [Transpose.Template("Transpose.Reflection.isInterface({this})")]
            get;
        }

        public extern bool IsArray
        {
            [Transpose.Template("Transpose.isArray(null, {this})")]
            get;
        }

        [Transpose.Template("Transpose.Reflection.getAttributes({this}, null, {inherit})")]
        public extern object[] GetCustomAttributes(bool inherit);

        [Transpose.Template("Transpose.Reflection.getAttributes({this}, {attributeType}, {inherit})")]
        public extern object[] GetCustomAttributes(Type attributeType, bool inherit);

        [Transpose.Template("Transpose.Reflection.isInstanceOfType({instance}, {this})")]
        public extern bool IsInstanceOfType(object instance);

        [Transpose.Template("Transpose.Reflection.isInstanceOfType({instance}, {type})")]
        public static extern bool IsInstanceOfType(object instance, Type type);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 31, 28)")]
        public extern MemberInfo[] GetMembers();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 31, {bindingAttr})")]
        public extern MemberInfo[] GetMembers(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 31, 28, {name})")]
        public extern MemberInfo[] GetMember(string name);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 31, {bindingAttr}, {name})")]
        public extern MemberInfo[] GetMember(string name, BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 1, 28)")]
        public extern ConstructorInfo[] GetConstructors();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 1, 284, null, {parameterTypes})")]
        public extern ConstructorInfo GetConstructor(Type[] parameterTypes);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, 28)")]
        public extern MethodInfo[] GetMethods();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, {bindingAttr})")]
        public extern MethodInfo[] GetMethods(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, 284, {name})")]
        public extern MethodInfo GetMethod(string name);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, {bindingAttr} | 256, {name})")]
        public extern MethodInfo GetMethod(string name, BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, 284, {name}, {parameterTypes})")]
        public extern MethodInfo GetMethod(string name, Type[] parameterTypes);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 8, {bindingAttr} | 256, {name}, {parameterTypes})")]
        public extern MethodInfo GetMethod(string name, BindingFlags bindingAttr, Type[] parameterTypes);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, 28)")]
        public extern PropertyInfo[] GetProperties();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, {bindingAttr})")]
        public extern PropertyInfo[] GetProperties(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, 284, {name})")]
        public extern PropertyInfo GetProperty(string name);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, {bindingAttr} | 256, {name})")]
        public extern PropertyInfo GetProperty(string name, BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, 284, {name}, {parameterTypes})")]
        public extern PropertyInfo GetProperty(string name, Type[] parameterTypes);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 16, {bindingAttr} | 256, {name}, {parameterTypes})")]
        public extern PropertyInfo GetProperty(string name, BindingFlags bindingAttr, Type[] parameterTypes);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 2, 28)")]
        public extern EventInfo[] GetEvents();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 2, {bindingAttr})")]
        public extern EventInfo[] GetEvents(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 2, 284, {name})")]
        public extern EventInfo GetEvent(string name);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 2, {bindingAttr} | 256, {name})")]
        public extern EventInfo GetEvent(string name, BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 4, 28)")]
        public extern FieldInfo[] GetFields();

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 4, {bindingAttr})")]
        public extern FieldInfo[] GetFields(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 4, 284, {name})")]
        public extern FieldInfo GetField(string name);

        [Transpose.Template("Transpose.Reflection.getMembers({this}, 4, {bindingAttr} | 256, {name})")]
        public extern FieldInfo GetField(string name, BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getNestedTypes({this})")]
        public extern Type[] GetNestedTypes();

        [Transpose.Template("Transpose.Reflection.getNestedTypes({this}, {bindingAttr})")]
        public extern Type[] GetNestedTypes(BindingFlags bindingAttr);

        [Transpose.Template("Transpose.Reflection.getNestedType({this}, {name})")]
        public extern Type GetNestedType(string name);

        [Transpose.Template("Transpose.Reflection.getNestedType({this}, {name}, {bindingAttr})")]
        public extern Type GetNestedType(string name, BindingFlags bindingAttr);

        /// <summary>
        /// When overridden in a derived class, returns the Type of the object encompassed or referred to by the current array, pointer or reference type.
        /// </summary>
        /// <returns>The Type of the object encompassed or referred to by the current array, pointer, or reference type, or null if the current Type is not an array or a pointer, or is not passed by reference, or represents a generic type or a type parameter in the definition of a generic type or generic method.</returns>
        [Transpose.Template("({this}.$elementType || null)")]
        public extern Type GetElementType();

        /// <summary>
        /// Gets a value indicating whether the current Type encompasses or refers to another type; that is, whether the current Type is an array, a pointer, or is passed by reference.
        /// </summary>
        public extern bool HasElementType
        {
            [Transpose.Template("(!!{this}.$elementType)")]
            get;
        }

        /// <summary>
        /// Returns a Type object representing a one-dimensional array of the current type, with a lower bound of zero.
        /// </summary>
        /// <returns>A Type object representing a one-dimensional array of the current type, with a lower bound of zero.</returns>
        [Transpose.Template("System.Array.type({this})")]
        public extern Type MakeArrayType();

        /// <summary>
        /// Returns a Type object representing an array of the current type, with the specified number of dimensions.
        /// </summary>
        /// <param name="rank">The number of dimensions for the array. This number must be less than or equal to 32.</param>
        /// <returns>An object representing an array of the current type, with the specified number of dimensions.</returns>
        [Transpose.Template("System.Array.type({this}, {rank})")]
        public extern Type MakeArrayType(int rank);

        [Transpose.Convention(Transpose.Notation.CamelCase)]
        public extern object Prototype { get; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Transpose.NonScriptable]
        public static extern Type GetTypeFromHandle(RuntimeTypeHandle typeHandle);

        /// <summary>
        /// Returns the names of the members of the current enumeration type.
        /// </summary>
        /// <returns>An array that contains the names of the members of the enumeration.</returns>
        [Transpose.Template("System.Enum.getNames({this})")]
        public virtual extern string[] GetEnumNames();

        /// <summary>
        /// Returns the name of the constant that has the specified value, for the current enumeration type.
        /// </summary>
        /// <param name="value">The value whose name is to be retrieved.</param>
        /// <returns>The name of the member of the current enumeration type that has the specified value, or null if no such constant is found.</returns>
        [Transpose.Template("System.Enum.getName({this}, {value})")]
        public virtual extern string GetEnumName(object value);

        /// <summary>
        /// Returns an array of the values of the constants in the current enumeration type.
        /// </summary>
        /// <returns>An array that contains the values. The elements of the array are sorted by the binary values (that is, the unsigned values) of the enumeration constants.</returns>
        [Transpose.Template("System.Enum.getValues({this})")]
        public virtual extern Array GetEnumValues();

        /// <summary>
        /// Returns the underlying type of the current enumeration type.
        /// </summary>
        /// <returns>The underlying type of the current enumeration.</returns>
        [Transpose.Template("System.Enum.getUnderlyingType({this})")]
        public virtual extern Type GetEnumUnderlyingType();

        /// <summary>
        /// Gets a value indicating whether the Type is declared public.
        /// </summary>
        public extern bool IsPublic
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 1)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is not declared public.
        /// </summary>
        public extern bool IsNotPublic
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 0)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether a class is nested and declared public.
        /// </summary>
        public extern bool IsNestedPublic
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 2)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is nested and declared private.
        /// </summary>
        public extern bool IsNestedPrivate
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 3)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is nested and visible only within its own family.
        /// </summary>
        public extern bool IsNestedFamily
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 4)")]
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the Type is nested and visible only within its own assembly.
        /// </summary>
        public extern bool IsNestedAssembly
        {
            [Transpose.Template("((Transpose.Reflection.getMetaValue({this}, \"att\", 0)  & 7)  == 5)")]
            get;
        }

        [Transpose.Template("{left} === {right}")]
        public static extern bool operator ==(Type left, Type right);

        [Transpose.Template("{left} !== {right}")]
        public static extern bool operator !=(Type left, Type right);

        [Transpose.Template("Transpose.getTypeName({this})")]
        public override extern string ToString();

        public extern bool IsValueType
        {
            [Transpose.Template("Transpose.Reflection.isValueType({this})")]
            get;
        }

        public extern bool IsPrimitive
        {
            [Transpose.Template("Transpose.Reflection.isPrimitive({this})")]
            get;
        }

        public extern static TypeCode GetTypeCode(Type type);
    }
}