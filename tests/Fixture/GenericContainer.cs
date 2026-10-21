using SharpBind;

namespace SharpBind.Fixture;

public class GenericContainer<T>
{
    /// <summary>A nested class specialized independently from its containing class.</summary>
    [PythonExport(typeof(int), typeof(double), Name = "NestedGeneric")]
    public class Inner<U>
    {
        /// <summary>Return the specialized inner value.</summary>
        /// <param name="value">Value to return.</param>
        [PythonExport]
        public static U Echo(U value) => value;
    }
}
