using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dapper.Contrib.Extensions;

namespace EmmaServer.Entities;

[Table("fornitori")] 
public class EmmaFornitori : INotifyPropertyChanged, IEntity
{
    // Flag per capire se il record è stato modificato dall'utente
    [Write(false)] // Dapper.Contrib lo ignorerà nelle query
    public bool IsDirty { get; set; } = false;

    private int _id;
    [Key]
    public int id 
    { 
        get => _id;
        set => SetProperty(ref _id, value, trackChange: false);
    }

    private string _descrizione = string.Empty;
    public string descrizione 
    { 
        get => _descrizione; 
        set => SetProperty(ref _descrizione, value); 
    }

    private string _riferimento = string.Empty;
    public string riferimento 
    { 
        get => _riferimento; 
        set => SetProperty(ref _riferimento, value); 
    }

    private string _tenant = string.Empty;
    public string tenant 
    { 
        get => _tenant; 
        set => SetProperty(ref _tenant, value); 
    }

    private int _score = 0;
    public int score 
    { 
        get => _score; 
        set => SetProperty(ref _score, value); 
    }

    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;


    // --- GESTIONE NOTIFICHE E TRACCIAMENTO MODIFICHE ---
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetProperty<T>(ref T storage, T value, bool trackChange = true, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return;

        storage = value;
        if (trackChange && id > 0) // Alza l'IsDirty solo se il record esisteva già sul DB
        {
            IsDirty = true;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}