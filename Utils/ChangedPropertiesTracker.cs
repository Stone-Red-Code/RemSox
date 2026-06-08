
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace RemSox.Utils;

public abstract class ChangedPropertiesTracker : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> properties = [];
    private readonly HashSet<string> changedPropertyNames = [];

    public IReadOnlyDictionary<string, object?> ChangedProperties
    {
        get
        {
            var changes = new Dictionary<string, object?>();
            foreach (var name in changedPropertyNames)
            {
                if (properties.TryGetValue(name, out var value))
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
            changedPropertyNames.Add(name);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void ClearChangedProperties() => changedPropertyNames.Clear();

    public bool IsPropertyChanged(string name) => changedPropertyNames.Contains(name);
}
