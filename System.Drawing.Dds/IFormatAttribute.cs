namespace System.Drawing.Dds
{
    internal interface IFormatAttribute<T>
    {
        T Format { get; }
    }
}
