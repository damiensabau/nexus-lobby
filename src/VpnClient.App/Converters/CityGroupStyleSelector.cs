using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VpnClient.App.Converters;

/// <summary>
/// Sous-dossier « ville » : avec en-tête quand le pays a plusieurs serveurs,
/// sans en-tête (serveurs affichés directement) quand la clé de ville est vide.
/// </summary>
public sealed class CityGroupStyleSelector : StyleSelector
{
    public Style? WithHeader { get; set; }
    public Style? WithoutHeader { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) =>
        item is CollectionViewGroup { Name: string city } && city.Length > 0 ? WithHeader : WithoutHeader;
}
