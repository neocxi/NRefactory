using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;

namespace ICSharpCode.NRefactory.ConsistencyCheck
{
  public static class Extensions
  {
    public static string SystemSensitivePath(this string path) {
      if (System.IO.Path.DirectorySeparatorChar == '/')
      {
        return path.Replace("\\", "/");
      }
      else
      {
        return path.Replace("/", "\\");
      }
    }

    public static XElement RemoveAllNamespaces(this XElement e)
    {
      return new XElement(e.Name.LocalName,
        (from n in e.Nodes()
           select ((n is XElement) ? RemoveAllNamespaces(n as XElement) : n)),
        (e.HasAttributes) ? 
      (from a in e.Attributes()
           where (!a.IsNamespaceDeclaration)
           select new XAttribute(a.Name.LocalName, a.Value)) : null);
    }

    public static XElement Get(this IEnumerable<XElement> els, string name)
    {
      try {
        return els.Where(x => x.Name == name).FirstOrDefault();
      } catch (Exception ex) {
        System.Diagnostics.Debugger.Break();
        throw ex;
      }
    }

    public static IEnumerable<XElement> GetMany(this IEnumerable<XElement> els, string name)
    {
      try {
        return els.Where(x => x.Name == name);
      } catch (Exception ex) {
        System.Diagnostics.Debugger.Break();
        throw ex;
      }
    }

  }
}
