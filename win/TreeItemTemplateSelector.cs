using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ace_run;

public class TreeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AppTemplate { get; set; }
    public DataTemplate? FolderTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is TreeViewNode node)
        {
            return node.Content switch
            {
                AppItemViewModel => AppTemplate,
                FolderViewModel => FolderTemplate,
                _ => base.SelectTemplateCore(item)
            };
        }
        return base.SelectTemplateCore(item);
    }
}
