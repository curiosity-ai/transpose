namespace System.ComponentModel
{
    [Transpose.External]
    public interface INotifyPropertyChanged : Transpose.ITransposeClass
    {
        event PropertyChangedEventHandler PropertyChanged;
    }

    [Transpose.Name("Function")]
    public delegate void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e);

    [Transpose.Convention(Member = Transpose.ConventionMember.Field | Transpose.ConventionMember.Method, Notation = Transpose.Notation.CamelCase)]
    [Transpose.External]
    public class PropertyChangedEventArgs : Transpose.ITransposeClass
    {
        public PropertyChangedEventArgs(string propertyName)
        {
        }

        public PropertyChangedEventArgs(string propertyName, object newValue)
        {
        }

        public PropertyChangedEventArgs(string propertyName, object newValue, object oldValue)
        {
        }

        public readonly string PropertyName;
        public readonly object OldValue;
        public readonly object NewValue;
    }
}