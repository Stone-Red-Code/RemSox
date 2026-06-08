
using System.ComponentModel;

namespace RemSox.Utils;

public abstract class ChangedPropertiesTracker : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> changedProperties = [];

    public IReadOnlyDictionary<string, object?> ChangedProperties => changedProperties;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AnyPropertyChanged => changedProperties.Count > 0;

    protected void SetProperty<T>(string name, ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            changedProperties[name] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void ClearChangedProperties() => changedProperties.Clear();

    public bool IsPropertyChanged(string name) => changedProperties.ContainsKey(name);
}
