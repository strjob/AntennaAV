using AntennaAV.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Xml.Linq;

namespace AntennaAV
{
    public class ViewLocator : IDataTemplate
    {

        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            // var type = Type.GetType(name);

            var viewTypeName = name + ", " + param.GetType().Assembly.GetName().Name;
            var type = Type.GetType(viewTypeName);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
