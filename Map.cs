using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using AdamMil.IO;
using GameLib.Video;

namespace Risky
{

#region Continent
/// <summary>Represents a collection of territories that a player is granted a bonus for controlling.</summary>
public sealed class Continent
{
  internal Continent(XmlNode continentNode, XmlNamespaceManager ns, Surface maskImage)
  {
    Territories = new TerritoryCollection();

    Name       = Xml.Attr(continentNode, "name");
    DraftBonus = Xml.Int32(continentNode, "draftBonus");

    // initialize the territories and compute the bounding box as the union of the territory bounding boxes
    foreach(XmlNode territoryNode in continentNode.SelectNodes("m:territory", ns))
    {
      Territory territory = new Territory(territoryNode, maskImage);
      Bounds = Territories.Count == 0 ? territory.Bounds : Rectangle.Union(Bounds, territory.Bounds);
      Territories.Add(territory);
    }
  }

  /// <summary>Gets the bounding rectangle of the continent, in pixels, relative to the map.</summary>
  public Rectangle Bounds
  {
    get; private set;
  }

  /// <summary>Gets the bonus number of armies granted each turn to the player who controls the entire continent.</summary>
  public int DraftBonus
  {
    get; private set;
  }

  /// <summary>Gets the name of the continent.</summary>
  public string Name
  {
    get; private set;
  }

  /// <summary>Gets the territories within the continent.</summary>
  public TerritoryCollection Territories
  {
    get; private set;
  }

  /// <summary>Returns the territory at the given coordinates, assuming the territory belongs to this continent, or
  /// null if no such territory belonging to this continent exists at the given coordinates.
  /// </summary>
  public Territory GetTerritory(int x, int y)
  {
    if(Bounds.Contains(x, y))
    {
      foreach(Territory territory in Territories)
      {
        if(territory.Contains(x, y)) return territory;
      }
    }
    return null;
  }
}
#endregion

#region Territory
/// <summary>Represents a territory on the map.</summary>
public sealed class Territory
{
  internal Territory(XmlNode territoryNode, Surface mask)
  {
    Name      = Xml.Attr(territoryNode, "name");
    Center    = ParseCoordinate(Xml.Attr(territoryNode, "center"));

    mask.Lock(); // lock the mask image so we can read its pixels more quickly

    // compute the bounds of the territory by flood-scanning the mask image (including all the islands)
    UpdateBounds(mask, Center);
    foreach(string coordinate in Xml.List(territoryNode, "islands")) UpdateBounds(mask, ParseCoordinate(coordinate));

    // allocate a bit mask where each bit represents a pixel within the bounding rectangle. bits are set if the pixel
    // belongs to the territory
    bitMask = new BitArray(Bounds.Width * Bounds.Height);

    // extract an image of the territory from the mask image. the image has all pixels belonging to the territory set
    // to white, and all other pixels transparent
    Image = new Surface(Bounds.Width, Bounds.Height, new PixelFormat(16, true));
    Image.Lock();
    CreateImage(mask, Center); // flood-scan the mask image again to create the image
    foreach(string coordinate in Xml.List(territoryNode, "islands")) CreateImage(mask, ParseCoordinate(coordinate));
    Image.Unlock();
    mask.Unlock();

    Neighbors = new TerritoryCollection();
  }

  /// <summary>Gets the bounding rectangle of the territory, in pixels, relative to the map.</summary>
  public Rectangle Bounds
  {
    get; private set;
  }

  /// <summary>Gets the "center" point of the territory, relative to the map. This may not be the actual center, but is
  /// where decorations such as the army count should be placed.
  /// </summary>
  public Point Center
  {
    get; private set;
  }

  /// <summary>Gets an image that fits into the territory's bounding rectangle, where all pixels belonging to the
  /// territory are white, and the rest are transparent.
  /// </summary>
  public Surface Image
  {
    get; private set;
  }

  /// <summary>Gets the name of the territory.</summary>
  public string Name
  {
    get; private set;
  }
  
  /// <summary>Gets a collection containing the neighbors of this territory (the territories that are considered
  /// adjacent for the purposes of moving or attacking).
  /// </summary>
  public TerritoryCollection Neighbors
  {
    get; private set;
  }

  /// <summary>Determines whether the given point, relative to the map, is within the territory.</summary>
  public bool Contains(int x, int y)
  {
    return Bounds.Contains(x, y) && bitMask[GetBitmaskIndex(x, y)];
  }

  /// <summary>Called for every pixel found during the flood-scan of the mask image.</summary>
  delegate void PixelHandler(int x, int y);

  /// <summary>Updates <see cref="Image"/> by flood-scanning pixels connected to the given point in the mask and
  /// setting the corresponding pixels in the image to white.
  /// </summary>
  void CreateImage(Surface mask, Point point)
  {
    uint white = Image.MapColor(Color.White);
    Flood(mask, point, (x, y) =>
    {
      bitMask[GetBitmaskIndex(x, y)] = true;
      Image.PutPixel(x-Bounds.X, y-Bounds.Y, white); // offset to convert from map to image coordinates
    });
  }

  /// <summary>Flood-scans the mask image starting from the given point, calling <paramref name="onPixel"/> for every
  /// connected point having the same color as the pixel under the given point (including the starting point).
  /// </summary>
  void Flood(Surface mask, Point point, PixelHandler onPixel)
  {
    HashSet<int> visited = new HashSet<int>();  // keep track of the indices of the pixels we've already been to
    Stack<int> toVisit = new Stack<int>(15000); // and the indices of the pixels we plan on visiting
    uint color = mask.GetPixelRaw(point); // get the color of the starting pixel

    toVisit.Push(point.X + point.Y*mask.Width); // push the starting pixel index
    do
    {
      // get the next pixel index and decode it back into x and y coordinates
      int position = toVisit.Pop(), x, y = Math.DivRem(position, mask.Width, out x);
      visited.Add(position); // mark that we've visited this pixel

      if(mask.GetPixelRaw(x, y) == color) // if the color matches the desired color...
      {
        onPixel(x, y); // call the pixel handler
        // and push the surrounding four pixels, if they haven't been visited already
        int left = position-1, right = position+1, up = position-mask.Width, down = position+mask.Width;
        if(x != 0 && !visited.Contains(left)) toVisit.Push(left);
        if(y != 0 && !visited.Contains(up)) toVisit.Push(up);
        if(x != mask.Width-1 && !visited.Contains(right)) toVisit.Push(right);
        if(y != mask.Height-1 && !visited.Contains(down)) toVisit.Push(down);
      }
    } while(toVisit.Count != 0); // while pixels remain to be visited
  }

  /// <summary>Converts a point on the map (assumed to be within the territory bounds) to the corresponding index
  /// within <see cref="bitMask"/>.
  /// </summary>
  int GetBitmaskIndex(int x, int y)
  {
    return (x-Bounds.Left) + (y-Bounds.Top)*Bounds.Width;
  }

  /// <summary>Flood-scans the mask image starting from the given point and expands the bounding rectangle to contain
  /// the matching area.
  /// </summary>
  void UpdateBounds(Surface mask, Point point)
  {
    if(Bounds.Width == 0) Bounds = new Rectangle(point.X, point.Y, 1, 1);
    Flood(mask, point, (x,y) => { Bounds = Rectangle.Union(Bounds, new Rectangle(x, y, 1, 1)); });
  }

  /// <summary>A bit array that determines for each pixel in the territory's bounds whether that pixel belongs to the
  /// territory.
  /// </summary>
  readonly BitArray bitMask;

  static Point ParseCoordinate(string coordinate)
  {
    int comma = coordinate.IndexOf(',');
    return new Point(int.Parse(coordinate.Substring(0, comma), CultureInfo.InvariantCulture),
                     int.Parse(coordinate.Substring(comma+1), CultureInfo.InvariantCulture));
  }
}
#endregion

#region TerritoryCollection
/// <summary>Represents a collection of territories.</summary>
public sealed class TerritoryCollection : List<Territory>
{
}
#endregion

#region Map
/// <summary>Represents a game map.</summary>
public sealed class Map
{
  /// <summary>Initializes a new <see cref="Map"/> given the path to an XML file describing it.</summary>
  public Map(string mapFile)
  {
    Continents  = new List<Continent>();
    Territories = new TerritoryCollection();

    // load and validate the map XML
    XmlDocument document = new XmlDocument();
    XmlNamespaceManager ns = new XmlNamespaceManager(document.NameTable);
    ns.AddNamespace("m", "http://adammil.net/Risky/map.xsd");
    document.Load(mapFile);
    document.Schemas.Add(XmlSchema.Read(new System.IO.StringReader(Resources.MapSchema), null));
    document.Validate((o,e) => { throw e.Exception; });

    // load the map and mask images
    string baseDirectory = Path.GetDirectoryName(mapFile);
    Image = new Surface(Path.Combine(baseDirectory, Xml.Attr(document.DocumentElement, "image")));
    Surface mask = new Surface(Path.Combine(baseDirectory, Xml.Attr(document.DocumentElement, "maskImage")));

    if(Image.Width != mask.Width || Image.Height != mask.Height)
    {
      throw new ArgumentException("The mask image and map image are of different sizes.");
    }

    // load the continents (and their corresponding territories)
    foreach(XmlNode continentNode in document.DocumentElement.SelectNodes("m:continents/m:continent", ns))
    {
      Continent continent = new Continent(continentNode, ns, mask);
      Continents.Add(continent);
      Territories.AddRange(continent.Territories); // add the continent's territories to the collection of all territories
    }

    // build a map to look up territiories by name, and ensure that all names are unique
    Dictionary<string,Territory> territoriesByName = new Dictionary<string,Territory>();
    foreach(Territory territory in Territories)
    {
      if(territoriesByName.ContainsKey(territory.Name))
      {
        throw new XmlSchemaValidationException("The territory name \"" + territory.Name +
                                               "\" was used multiple times.");
      }
      territoriesByName[territory.Name] = territory;
    }

    // go through the territory connections and make the connected territories neighbors
    foreach(Match m in connectionRe.Matches(document.DocumentElement.SelectSingleNode("m:connections", ns).InnerText))
    {
      Territory t1 = GetTerritoryById(document, ns, territoriesByName, m.Groups[1].Value);
      Territory t2 = GetTerritoryById(document, ns, territoriesByName, m.Groups[2].Value);
      if(!t1.Neighbors.Contains(t2))
      {
        t1.Neighbors.Add(t2);
        t2.Neighbors.Add(t1);
      }
    }
  }

  /// <summary>Gets a collection of the continents on the map.</summary>
  public List<Continent> Continents
  {
    get; private set;
  }

  /// <summary>Gets the map image.</summary>
  public Surface Image
  {
    get; private set;
  }

  /// <summary>Gets a collection of all territories on the map.</summary>
  public TerritoryCollection Territories
  {
    get; private set;
  }

  /// <summary>Gets the territory corresponding to the given point on the map, or null if the point does not correspond
  /// to any territory.
  /// </summary>
  public Territory GetTerritory(int x, int y)
  {
    foreach(Continent continent in Continents)
    {
      Territory territory = continent.GetTerritory(x, y);
      if(territory != null) return territory;
    }
    return null;
  }

  /// <summary>Looks up and returns the territory with the given ID, or throws an exception if the a territory with
  /// the given ID cannot be found.
  /// </summary>
  Territory GetTerritoryById(XmlDocument document, XmlNamespaceManager ns,
                             Dictionary<string,Territory> territoriesById, string id)
  {
    XmlNode node = document.SelectSingleNode("//m:territory[@id='" + id + "']", ns);
    if(node == null) throw new XmlSchemaValidationException("No territory exists with id \"" + id + "\".");
    return territoriesById[Xml.Attr(node, "name")];
  }

  /// <summary>Matches a pair of territory IDs corresponding to a connection between them.</summary>
  static readonly Regex connectionRe = new Regex(@"(\w+)\s+(\w+)", RegexOptions.Singleline);
}
#endregion

} // namespace Risky