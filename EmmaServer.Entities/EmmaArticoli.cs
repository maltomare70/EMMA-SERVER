using Dapper.Contrib.Extensions;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace EmmaServer.Entities;

[Dapper.Contrib.Extensions.Table("articoli")] 
public record EmmaArticoli: INotifyPropertyChanged, IEntity
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
    
    private string _codice = string.Empty;
    [Column(TypeName = "varchar(256)")]
    public string codice
    { 
        get => _codice; 
        set => SetProperty(ref _codice, value); 
    }

    
    private string _descrizione = string.Empty;
    [Column(TypeName = "varchar(256)")]
    public string descrizione 
    { 
        get => _descrizione; 
        set => SetProperty(ref _descrizione, value); 
    }
    
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    
    private string _rifcodice = string.Empty;
    public string rifcodice 
    { 
        get => _rifcodice; 
        set => SetProperty(ref _rifcodice, value); 
    }
    
    
    private string _rifdescrizione = string.Empty;
    [Column(TypeName = "varchar(256)")]
    public string rifdescrizione 
    { 
        get => _rifdescrizione; 
        set => SetProperty(ref _rifdescrizione, value); 
    }
    
    private int _scorecodice= 0;
    public int scorecodice 
    { 
        get => _scorecodice; 
        set => SetProperty(ref _scorecodice, value); 
    }
    
    private int _scoredescrizione= 0;
    public int scoredescrizione 
    { 
        get => _scoredescrizione; 
        set => SetProperty(ref _scoredescrizione, value); 
    }

    [Column(TypeName = "varchar(100)")]
    public string tenant { get; set; } = string.Empty;
    
    private int _idfornitore= 0;
    [Column(TypeName = "varchar(100)")]
    public int idfornitore 
    { 
        get => _idfornitore; 
        set => SetProperty(ref _idfornitore, value); 
    }

    
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