using System.ComponentModel;

namespace RemSox.Kernel.Utils;

/// <summary>
/// Remembers whether any property changed since the window last drew the element, so unchanged
/// elements are not drawn again.
/// </summary>
public abstract class ChangedPropertiesTracker : INotifyPropertyChanged
{
    private readonly HashSet<string> changedPropertyNames = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AnyPropertyChanged => changedPropertyNames.Count > 0;

    protected void SetProperty<T>(string name, ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _ = changedPropertyNames.Add(name);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void ClearChangedProperties()
    {
        changedPropertyNames.Clear();
    }
}
