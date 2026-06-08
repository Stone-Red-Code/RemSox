using System.ComponentModel;

namespace RemSox.Utils;

public abstract class ChangedPropertiesTracker : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> properties = [];
    private readonly HashSet<string> changedPropertyNames = [];

    public IReadOnlyDictionary<string, object?> ChangedProperties
    {
        get
        {
            Dictionary<string, object?> changes = [];
            foreach (string name in changedPropertyNames)
            {
                if (properties.TryGetValue(name, out object? value))
                {
                    changes[name] = value;
                }
            }
            return changes;
        }
    }

    public IReadOnlyDictionary<string, object?> AllProperties => properties;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AnyPropertyChanged => changedPropertyNames.Count > 0;

    protected void SetProperty<T>(string name, ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            properties[name] = value;
            _ = changedPropertyNames.Add(name);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void ClearChangedProperties()
    {
        changedPropertyNames.Clear();
    }

    public bool IsPropertyChanged(string name)
    {
        return changedPropertyNames.Contains(name);
    }
}
