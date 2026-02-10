using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CommunityToolkit.Mvvm.ComponentModel;

public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
{
	public event PropertyChangedEventHandler? PropertyChanged;

	public event PropertyChangingEventHandler? PropertyChanging;

	protected void OnPropertyChanging([CallerMemberName] string? propertyName = null)
	{
		this.PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
	}

	protected void OnPropertyChanging(PropertyChangingEventArgs eventArgs)
	{
		this.PropertyChanging?.Invoke(this, eventArgs);
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	protected void OnPropertyChanged(PropertyChangedEventArgs eventArgs)
	{
		this.PropertyChanged?.Invoke(this, eventArgs);
	}

	protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, newValue))
		{
			return false;
		}
		OnPropertyChanging(propertyName);
		field = newValue;
		OnPropertyChanged(propertyName);
		return true;
	}
}
