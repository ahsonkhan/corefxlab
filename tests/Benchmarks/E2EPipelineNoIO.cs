// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Xunit.Performance;
using System.Collections.Generic;
using Xunit;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Text;

public partial class E2EPipelineTests
{
    public static HashSet<string> cities = new HashSet<string>();

    public static HashSet<ReadOnlyMemory<char>> citiesMem = new HashSet<ReadOnlyMemory<char>>();

    public static string testString = "stringignored, Taber,Tableland.Tacheeda Tabor)Taghum(HELLO ignore";

    public static string[] myCities =
        {
            "Tabaret", "Tabati�re", "Taber", "Table Bay", "Table Head", "Table Head", "Tableland", "Tabor", "Tabor", "Tabusintac", "Tach�", "Tach�", "Tacheeda", "Tachie", "Tacks Beach", "Tadanac", "Tadmor", "Tadmore", "Tadoule Lake", "Tadoussac", "Taft", "Tafton", "Taggart", "Taghum", "Tagish", "Tahltan", "Tahsis", "Taillon", "Taits Beach", "Takhini", "Takhini Hotspring", "Takipy", "Takla", "Takla Landing", "Tako", "Taku", "Takysie Lake", "Talbot", "Talbot", "Talbot", "Talbot", "Talbotville Royal", "Talcville", "Tallheo", "Tallman", "Tall Pines", "Talmage", "Talon", "Talon", "Taloyoak", "Talzie", "Tamarac Estates", "Tamarack", "Tamarisk", "Tam O'Shanter", "Tam O'Shanter Ridge", "Tamworth", "Tancook Island", "Tancredia", "Tangent", "Tangier", "Tangleflags", "Tanglewood", "Tanglewood Beach", "Tanguay", "Tanguishene", "Tankville", "Tannahill", "Tanners Settlement", "Tannin", "Tanquary Camp", "Tansley", "Tansleyville", "Tantallon", "Tantallon", "Tanu", "Tapley", "Tapley", "Tapley Mills", "Tapleys", "Tapleys Mills", "Tapleytown", "Taplow", "Tappen", "Tara", "Taradale", "Taradale", "Tarantum", "Tara Siding", "Tarbert", "Tarbot", "Tarbotvale", "Targettville", "Tar Island", "Tarnopol", "Tarrtown", "Tarrys", "Tartan", "Tartan Lane", "Tartigou", "Tarzwell", "Taschereau", "Taschereau", "Taschereau-Fortier", "Ta:shiis", "Tashme", "Tashota", "Tasialuup Itillinga", "Tasialuup Sijjanga", "Tasikutaaraaluup Sitialungit", "Tasiqanngituq", "Tasirmiuviup Sijjangit", "Tasiujaq", "Tasu", "Ta Ta Creek", "Tatagwa", "Tatainaq", "Tatalrose", "Tatamagouche", "Tatamagouche Mountain", "Tate", "Tate Corners", "Tatehurst", "Tatla Lake", "Tatlayoko Lake", "Tatlock", "Tatlow", "Tatnall", "Tatogga", "Tatsfield", "Tatton", "Tattons Corner", "Taunton", "Taurus", "Tavani", "Taverna", "Tavistock", "Tawa", "Tawatinaw", "Taxis River", "Tay", "Tay", "Tay Creek", "Tay Falls", "Taylor", "Taylor", "Taylor", "Taylor Beach", "Taylor Corners", "Taylor Road", "Taylors", "Taylor's Bay", "Taylor's Head", "Taylors Head", "Taylorside", "Taylors Road", "Taylor Statten", "Taylorton", "Taylor Village", "Taylorville", "Tay Mills", "Taymouth", "Tay Settlement", "Tayside", "Tay Valley", "Tchesinkut Lake", "Tc�b�tik S�gik", "Tcorecik", "Tea Cove", "Teahans Corner", "Tea Hill", "Teakerne Arm", "Teakle", "Tebo", "Tecumseh", "Teddington", "Teeds Mill", "Teeds Mills", "Tee Lake", "Teepee", "Teepee Creek", "Tees", "Teeswater", "Teeterville", "Tehkummah", "Teko", "Telachick", "Telegraph Cove", "Telegraph Creek", "Telegraph Point", "Telfer", "Telford", "Telford", "Telfordville", "Telkwa", "Tellier", "Telly Road Crossing", "Teltaka", "Temagami", "Temagami North", "T�miscaming", "T�miscouata-sur-le-Lac", "Temiskaming Shores", "Temperance Vale", "Temperanceville", "Tempest", "Temple", "Temple", "Temple", "Temple Hill", "Templeman", "Templemead", "Templeton", "Templeton-Est", "Templeton-Ouest", "Tempo", "Tenaga", "Tenby", "Tenby Bay", "Ten Mile", "Ten Mile", "Tenmile Cabin", "Ten Mile Creek", "Tenmile House", "Ten Mile Lake", "Ten Mile Lake", "Tennant Cove", "Tennants Cove", "Tennessee", "Tennion", "Tennycape", "Tennyson", "Tent City", "Terence", "Terence Bay", "Terence Bay River", "Terminal Beach", "Terminus", "Terrace", "Terrace Bay", "Terrace Heights", "Terrace Heights", "Terrace Hill", "Terra Cotta", "Terrains de L'�v�que", "Terra Nova", "Terra Nova", "Terra Nova", "Terrasse-Bellevue", "Terrasse-Bigras", "Terrasse-Campbell", "Terrasse-Charbonneau", "Terrasse-de-Luxe", "Terrasse-des-Pins", "Terrasse-Dusseault", "Terrasse-Duvernay", "Terrasse-Legault", "Terrasse-Pr�fontaine", "Terrasse-Raymond", "Terrasse-Robillard", "Terrasse-Saint-Fran�ois", "Terrasse-Samson", "Terrasse-Th�oret", "Terrasse-Vaudreuil", "Terra View Heights", "Terre-�-Fer", "Terrebonne", "Terrenceville", "Terre Noire", "Terres-Rompues", "Territoire-Coburn", "Teslin", "Teslin Crossing", "Teslin Lake", "Teslin River", "Tesseralik", "Tessier", "Teston", "Tetachuk", "Tetagouche North", "Tetagouche North Side", "Tetagouche South", "Tetagouche South Side", "Tetana", "T�te-�-la-Baleine", "T�te-de-l'�le", "T�te Jaune", "T�te Jaune Cache", "T�treaultville", "Teulon", "Teviotdale", "Tewkesbury", "Texas", "Thabor", "Thackeray", "Thaddeus", "Thalberg", "Thalia", "Thamesford", "Thames River Siding", "Thames Road", "Thamesville", "Thaxted", "Thayendanegea", "The Annex", "The Back Settlement", "The Battery", "The Battery", "The Beaches", "The Beaches", "The Beaver Lodge", "The Block", "The Bluff", "The Bluffs", "The Boyne", "The Broads", "The Bush", "The Cache", "The Cedars", "The Cedars", "The Corners", "The Corra", "The Cottage", "The Cottages", "The Cross", "The Crossroads", "The Crutch", "The Delta", "The Depot", "Thedford", "The District of Lakeland No. 521", "The Dock", "The Donovan", "The Droke", "The Elbow", "The Falls", "The Flats", "The Flume", "The Forks", "The Fort", "The Front", "The Gib", "The Glades", "The Glebe", "The Glen", "The Glen", "The Golden Mile", "The Gore", "The Gore", "The Gorge", "The Grange", "The Grant", "The Gravels", "The Green", "The Grove", "The Groves", "The Gully", "The Gully", "The Halfway", "The Halfway", "The Hawk", "The Highlands", "The Highlands", "The Houser", "The Island", "The Junction", "The Junction", "The Keys", "The Kingsway", "The Lakehead", "The Ledge", "The Light", "Thelma", "The Lodge", "The Lookoff", "The Lots", "The Manor", "The Maples", "The Maples", "The Meadows", "The Meadows", "The Medway", "The Mines", "The Motion", "The Narrows", "The Narrows", "The Ninth", "Theodore", "Theodosia Arm", "The Outlet", "The Pas", "The Pas Airport", "The Pines", "The Points West Bay", "The P Patch", "The Range", "Theresa", "Theriault", "Theriault", "Th�riault", "The Ridge", "The Ridge", "Therien", "The Rock", "The Rollway", "The Sheds", "The Sixth", "The Slash", "Thessalon", "The Tannery", "Thetford Mines", "Thetford-Partie-Sud", "The Thicket", "The Tickles", "Thetis Island", "The Two Rivers", "The Willows", "Thibaudeau", "Thibault", "Thibeault Terrace", "Thibeauville", "Thicket Portage", "Third Lake", "Thirsk", "Thirty Mountain Church", "Thistle", "Thistle Creek", "Thistletown", "Thivierge", "Thoburn", "Thode", "Thomas", "Thomas Brook", "Thomasburg", "Thomaston Corner", "Thomasville", "Thom Bay", "Thomond", "Thompson", "Thompson", "Thompson", "Thompson", "Thompson Corner", "Thompson Hill", "Thompson Junction", "Thompson Lake", "Thompson Lake", "Thompson Landing", "Thompsons Mills", "Thompson Sound", "Thompsonville", "Thomson Hill", "Thomson Station", "Thomstown", "Thorah Beach", "Thorah Island", "Thorburn", "Thorburn Lake", "Thorburn Road", "Thordarson", "Thorel House", "Thorhild", "Thorlake", "Thor Lake", "Thornbrook", "Thornbury", "Thornby", "Thorncliff", "Thorncliffe", "Thorncliffe", "Thorncliffe", "Thorncliffe", "Thorncrest Village", "Thorndale", "Thorndale", "Thorndyke", "Thorne", "Thorne", "Thorne", "Thorne Centre", "Thorne Cove", "Thorne Lake", "Thorner", "Thornes Cove", "Thornetown", "Thornhill", "Thornhill", "Thornhill", "Thornhill", "Thornlea", "Thornloe", "Thornton", "Thornton Yard", "Thornyhurst", "Thorold", "Thorold Park", "Thorold South", "Thoroughfare", "Thorpe", "Thorsby", "Thorton Woods", "Thrasher", "Thrasher's Corners", "Three Arms", "Three Bridges", "Three Brooks", "Three Brooks", "Three Creeks", "Three Fathom Harbour", "Three Forks", "Three Hills", "Threehouse", "Three Mile Plains", "Three Mile Rock", "Three Tree Creek", "Three Valley", "Thresher Corners", "Throne", "Throoptown", "Thrope", "Thrums", "Thunder Bay", "Thunder Bay", "Thunder Beach (Baie-du-Tonnerre)", "Thunderbird", "Thunderchild", "Thunder Creek", "Thunder Hill", "Thunderhill Junction", "Thunder River", "Thurlow", "Thurlow", "Thurso", "Thurston Bay", "Thurston Harbour", "Thurstonia Park", "Thwaites", "Thwaytes", "Tibbets", "Tibbos Hill", "Tiblemont", "Ticehurst Corners", "Tichborne", "Tichfield", "Tichfield Junction", "Tickle Cove", "Tickle Harbour", "Tickle Harbour Station", "Tickle Road", "Tickles", "Ticouap�", "Tidal", "Tiddville", "Tide Head", "Tidnish", "Tidnish Bridge", "Tidnish Cross Roads", "Tieland", "Tiffin", "Tiffin", "Tiger", "Tiger Hills", "Tiger Lily", "Tignish", "Tignish Corner", "Tignish Shore", "Tiili Landing", "Tiilis", "Tiilis Landing", "Tika", "Tilbury", "Tilbury", "Tilbury Centre", "Tilbury Dock", "Tilbury Station", "Tilden Lake", "Tilley", "Tilley", "Tilley", "Tilley Road", "Tillicum", "Tillsonburg", "Tillsonburg Junction", "Tilly", "Tilney", "Tilsonburg", "Tilston", "Tilt Cove", "Tilt Cove", "Tilting", "Tilton", "Tilts", "Timagami", "Timagami Lodge", "Timber Bay", "Timberlea", "Timberlea Trail", "Timberlost", "Timber River", "Timberton", "Timbrell", "Timeu", "Timmijuuvinirtalik", "Timmins", "Timmins-Porcupine", "Tims Harbour", "Tincap", "Tinchebray", "Tingwick", "Tinker", "Tintagel", "Tintern", "Tin Town", "Tiny", "Tioga", "Tionaga", "Tipaskan", "Tipella", "Tipitu Pachistuwakan", "Tipperary", "Tisdale", "Tisdall", "Titanic", "Tittle Road", "Titus", "Titus Mills", "Titusville", "Tiverton", "Tiverton", "Tl'aaniiwa'a", "Tlakmaqis", "Tlell", "Tl'isnachis", "Tl'itsnit", "Tloohat-a", "Toad River", "Toanche", "Toba", "Tobacco Lake", "Tobermory", "Tobey", "Tobiano", "Tobin Lake", "Tobique Narrows", "Tobique River", "Toby Creek", "Tochty", "Tod", "Todaro", "Tod Creek", "Todds Island", "Tod Inlet", "Todmorden", "Tofield", "Tofino", "Togo", "Toimela", "Tokay", "Toketic", "Toledo", "Tolland", "Tollendal", "Tollgate", "Tolman", "Tolmie", "Tolmies Corners", "Tolsmaville", "Tolsta", "Tolstad", "Tolstoi", "Tomahawk", "Tomahawk Lake", "Tomawapocokanan", "Tom Cod", "Tomelin Bluffs", "Tomifobia", "Tomiko", "Tomkinsville", "Tomkinville", "Tom Longboat Corners", "Tompkins", "Tom-Rule's Ground", "Tomslake", "Toms Savannah", "Tomstown", "Tondern", "Toney Mills", "Toney River", "Toniata", "Tonkas", "Tonkin", "Toogood Arm", "Tooleton", "Tootinaowaziibeeng", "Topcliff", "Tophet", "Topland", "Topley", "Topley Landing", "Topping", "Topsail", "Torbay", "Torbay", "Tor Bay", "Torbrook", "Torbrook East", "Torbrook Mines", "Torbrook West", "Torch River", "Torlea", "Tormore", "Tornea", "Toronto", "Toronto", "Toronto Junction", "Torquay", "Torrance", "Torrent", "Torrington", "Torryburn", "Tory Hill", "Toslow", "Tothill", "Totnes", "Totogon", "Tottenham", "Totzke", "Touchwood", "Touraine", "Tour-Bois-Blanc", "Tour-Boissinot", "Tour-Butney", "Tour-Chicotte", "Tour-de-Jupiter", "Tour-de-la-Rivi�re-�-l'Huile", "Tour-des-Hauteurs", "Tour-des-Lacs-George", "Tour-du-Cinquante-Milles", "Tour-du-Lac-Orignal", "Tour-du-Nord", "Tourelle", "Tour-Fraser", "Tour-Galienne", "Tour-Maher", "Tourond", "Tour-Patap�dia", "Tour-Rita", "Tour-Sept-Milles", "Tour-Tableau", "Tour-Val-Marie", "Tourville", "Toutes Aides", "Towdystan", "Tower Estates", "Tower Lake", "Tower Road", "Towers", "Tow Hill", "Town Hall", "Town Lake", "Townline", "Townsend", "Townsend", "Townsend Centre", "Toyehill", "Toyes Hill", "Tracadie", "Tracadie", "Tracadie", "Tracadie Beach", "Tracadie Camp", "Tracadie Cross", "Tracadie Road", "Tracadie-Sheila", "Tracard", "Tracey Mills", "Tracy", "Tracy", "Tracy Depot", "Tracyville", "Traders Cove", "Trafalgar", "Trafalgar", "Trafalgar", "Trafalgar Heights", "Trafford", "Trail", "Trails End", "Trait-Carr�", "Trait-Carr�", "Tralee", "Tramore", "Tramping Lake", "Tranquility", "Tranquille", "Transcona", "Transcona", "Transfiguration", "Trapper's Landing", "Trapp Road", "Travellers Rest", "Travers", "Traverse Bay", "Traverse-du-Remous", "Traverse Landing", "Traverston", "Travor Road", "Traynor", "Traytown", "Traytown", "Treadwell", "Treat", "Trecastle", "Tr�cesson", "Tree Farm", "Treelon", "Treesbank", "Treesbank Ferry", "Trefoil", "Tregarva", "Treherne", "Tremaine", "Tremaudan", "Tremblay", "Tremblay", "Tremblay", "Tremblay Settlement", "Trembleur", "Trembley", "Trembowla", "Tremont", "Trenche", "Trend Village", "Trenholm", "Trenholme", "Trentham", "Trenton", "Trenton", "Trenton Junction", "Trent River", "Trenville", "Tr�panier", "Trepassey", "Tr�s-Pr�cieux-Sang-de-Notre-Seigneur", "Tr�s-Saint-R�dempteur", "Tr�s-Saint-Sacrement", "Trevelyan", "Trevessa Beach", "Trewdale", "Triangle", "Triangle", "Tribune", "Trilby", "Tring", "Tring-Jonction", "Trinit�-des-Monts", "Trinity", "Trinity", "Trinity", "Trinity Bay North", "Trinity East", "Trinity Park", "Trinity Valley", "Trinny Cove", "Triple Bay Park", "Tripoli", "Tripp Settlement", "Triquet", "Tristram", "Triton", "Triton East", "Triton Island", "Triton West", "Triwood", "Trochu", "Trois-Lacs", "Trois-Lacs", "Trois-Pistoles", "Trois-Rives", "Trois-Rivi�res", "Trois-Rivi�res-Ouest", "Trois-Ruisseaux", "Trois-Ruisseaux", "Trois-Saumons", "Trois-Saumons-Station", "Trossachs", "Trottier", "Trou-�-Gardner", "Trou-�-P�pette", "Troup", "Trout Brook", "Trout Brook", "Trout Creek", "Trout Creek", "Trout Lake", "Trout Lake", "Trout Lake", "Trout Lake", "Trout Lake", "Trout Mills", "Trout River", "Trout River", "Trout River", "Trout Stream", "Trouty", "Trowbridge", "Troy", "Troy", "Troy", "Truax", "Trudeau", "Trudel", "Trudel", "Truemanville", "Truman", "Trump", "Trump Islands", "Truro", "Truro Heights", "Trutch", "Tryon", "Tryon Settlement", "Tsawwassen", "Tsawwassen Beach", "Ts'axq'oo-is", "Tsay Keh Dene", "Tshak Penatu Epit", "Tshiahahtunekamuk", "Tshiahkuehihat Peniauiht", "Tshinuatipish", "Tsiigehtchic", "Ts'iispoo-a", "Tuam", "Tuapaaluit", "Tubbs Corners", "Tuberose", "Tuchitua", "Tucker", "Tucks", "Tudor", "Tuffnell", "Tuft Cove", "Tufts Cove", "Tuftsville", "Tugaske", "Tugtown", "Tuktoyaktuk", "Tulameen", "Tulita", "Tullamore", "Tulliby Lake", "Tullis", "Tullochgorum", "Tullymet", "Tulsequah", "Tumbler Ridge", "Tummel", "Tunaville", "Tungsten", "Tuniit", "Tunis", "Tunnel", "Tunnel", "Tunney's Pasture", "Tunstall", "Tununuk", "Tupialuviniq", "Tupirvialuit", "Tupirviit", "Tupirvikallak", "Tupirvituqaaluttalik", "Tupirviturlik", "Tupirvivinirtalik", "Tupper", "Tupper", "Tupper Lake", "Tupperville", "Tupperville", "Turbine", "Turcot", "Turgeon", "Turgeon Station", "Turin", "Turin", "Turkey Point", "Turks Cove", "Turnberry", "Turnbull", "Turner", "Turner", "Turners", "Turner's Bight", "Turners Corners", "Turner Settlement", "Turnertown", "Turner Valley", "Turnerville", "Turnip Cove", "Turnor Lake", "Turn-Up Juniper", "Turriff", "Turtle", "Turtle Beach", "Turtle Creek", "Turtle Dam", "Turtleford", "Turtleford Junction", "Turtle Lake", "Turtle Lake South Bay", "Turtle Valley", "Tuscany", "Tusket", "Tusket Falls", "Tutela", "Tutela Heights", "Tutshi", "Tuttle", "Tuttles Hill", "Tuttusivik", "Tuurngaup Illuvininga", "Tuwanek", "Tuxedo", "Tuxedo Park", "Tuxedo Park", "Tuxford", "Tway", "Tweed", "Tweedie", "Tweedie", "Tweedie Brook", "Tweedle Place", "Tweedside", "Tweedside", "Tweedsmuir", "Tweedsmuir", "Tweedsmuir", "Tweedsmuir Village", "Twelve O'Clock Point", "Twentymile Cabin", "Twentyone Mile", "Twentysix Mile", "Twentysix Mile Landing", "Twickenham", "Twidwell Bend", "Twillingate", "Twin Brae", "Twin Butte", "Twin Butte", "Twin City", "Twin Creeks", "Twin Elm", "Twin Falls", "Twin Falls", "Twining", "Twin Islands", "Twin Lakes Beach", "Twin River", "Twin Rock Valley", "Twin Valley", "Two Brooks", "Two Creeks", "Two Creeks", "Two Guns", "Two Hills", "Two Islands", "Two Lakes", "Twomey", "Two Mile", "Two Mile Corner", "Two O'Clock", "Two Rivers", "Two Rivers", "Two Rivers", "Tyandaga", "Tye", "Tyee", "Tyndall", "Tyndall Park", "Tyndal Road", "Tynehead", "Tynemouth Creek", "Tyner", "Tyneside", "Tyne Valley", "Tyotown", "Tyranite", "Tyrconnell", "Tyrone", "Tyrone", "Tyrrell", "Tyrrell", "Tyson", "Tyvan", "Tzouhalem", "Tzuhalem"
            };


    private static void PopulateCities()
    {
        if (cities.Count == 0)
        {
            Console.WriteLine("=== Populating ===");
            for (int i = 0; i < myCities.Length; i++)
            {
                string city = myCities[i];
                if (city.IndexOf(' ') == -1)
                {
                    cities.Add(city);
                    var mem = city.AsReadOnlyMemory();
                    //Console.WriteLine(mem.GetHashCode());
                    citiesMem.Add(mem);
                    testString += "." + city;
                }
            }
        }
        Console.WriteLine(cities.Count + " : " + testString.Length);
    }

    [Benchmark(InnerIterationCount = 2)]
    private static void TestingOriginalPopulate()
    {
        int count = 0;
        foreach (var iteration in Benchmark.Iterations)
        {
            
            using (iteration.StartMeasurement())
            {
                HashSet<string> citiesString = new HashSet<string>();   
                for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                {
                    
                    for (int k = 0; k < myCities.Length; k++)
                    {
                        string city = myCities[k];
                        if (city.IndexOf(' ') == -1)
                        {
                            citiesString.Add(city);
                        }
                    }
                    
                }
                count = citiesString.Count;
                    citiesString.Clear();
            }
            
        }
        Console.WriteLine("WOW1 " + count);
    }


    [Benchmark(InnerIterationCount = 2)]
    private static void TestingMemoryPopulate()
    {
        int count = 0;
        foreach (var iteration in Benchmark.Iterations)
        {
            
            using (iteration.StartMeasurement())
            {
                HashSet<ReadOnlyMemory<char>> citiesMemory = new HashSet<ReadOnlyMemory<char>>();
                for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                {
                    
                    for (int k = 0; k < myCities.Length; k++)
                    {
                        string city = myCities[k];
                        if (city.IndexOf(' ') == -1)
                        {
                            citiesMemory.Add(city.AsReadOnlyMemory());
                        }
                    }
                    
                }
                count = citiesMemory.Count;
                    
                    citiesMemory.Clear();
            }
            
        }
        Console.WriteLine("WOW2 " + count);
    }



    [Benchmark(InnerIterationCount = 1000)]
    private static void TestingOriginal()
    {
        PopulateCities();

        Console.WriteLine("TestString Original - " + testString);

        foreach (var iteration in Benchmark.Iterations)
        {
            using (iteration.StartMeasurement())
            {
                for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                {
                    GetCityFromQuery(testString);
                }
            }
        }
    }

    [Benchmark(InnerIterationCount = 1000)]
    private static void TestingSpan()
    {
        PopulateCities();

        Console.WriteLine("TestString Span - " + testString);

        Console.WriteLine(GetCityFromQuery(testString).Length);
        Console.WriteLine(GetCityFromQueryMemory(testString.AsReadOnlyMemory()).Length);
        Console.WriteLine(GetCityFromQuerySpan(testString).Length);

        Assert.Equal(GetCityFromQuery(testString), GetCityFromQueryMemory(testString.AsReadOnlyMemory()));

        foreach (var iteration in Benchmark.Iterations)
        {
            using (iteration.StartMeasurement())
            {
                for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                {
                    GetCityFromQueryMemory(testString.AsReadOnlyMemory());
                }
            }
        }
    }


    /*public static void Write<T>(this IBufferWriter<T> bufferWriter, ReadOnlySpan<T> source)
    {
        Span<T> destination = bufferWriter.GetSpan();

        // Fast path, try copying to the available memory directly
        if (source.Length <= destination.Length)
        {
            source.CopyTo(destination);
            bufferWriter.Advance(source.Length);
            return;
        }

        if (source.Length > 0)
        {
            if (destination.Length == 0)
            {
                destination = bufferWriter.GetSpan(source.Length);
            }

            int writeSize = Math.Min(destination.Length, source.Length);
            source.Slice(0, writeSize).CopyTo(destination);
            bufferWriter.Advance(writeSize);
            source = source.Slice(writeSize);

            while (source.Length > 0)
            {
                destination = bufferWriter.GetSpan(source.Length);

                writeSize = Math.Min(destination.Length, source.Length);
                source.Slice(0, writeSize).CopyTo(destination);
                bufferWriter.Advance(writeSize);
                source = source.Slice(writeSize);
            }
        }
    }*/

    public static string GetCityFromQuery(string PlaceFeatures)
    {
        if (string.IsNullOrEmpty(PlaceFeatures)) return string.Empty;

        string[] words = PlaceFeatures.Split(' ', ',', '.', '(', ')');

        int count = 0;

        for (int i = 0; i < words.Length; i++)
        {
            //if (cities.Contains(words[i]))
            //{
                words[count++] = words[i];
            //}
        }

        return string.Join("~", words, 0, count);
    }

    public static char[] splitvalues = { ' ', ',', '.', '(', ')' };

    public static byte[] splitvaluesBytes = { (byte)' ', (byte)',', (byte)'.', (byte)'(', (byte)')' };

    public static ReadOnlyMemory<char> temp;

    public static string GetCityFromQueryMemory(ReadOnlyMemory<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];

        int count = 0;
        int startIndex = 0;
        ReadOnlySpan<char> featureSpan = PlaceFeatures.Span;
        while (startIndex < PlaceFeatures.Length)
        {
            int index = MyIndexOfAny(featureSpan);
            //Console.WriteLine(index + " : " + startIndex + " : " + count);
            if (index == -1)
            {
                //Console.WriteLine(new string(PlaceFeatures.Slice(startIndex).Span));
                DoSomething(PlaceFeatures.Slice(startIndex));
                //if (citiesMem.Contains(PlaceFeatures.Slice(startIndex)))
                //{
                    featureSpan.CopyTo(words.Slice(count));
                    count += featureSpan.Length;
                //}
                break;
            }
            DoSomething(PlaceFeatures.Slice(startIndex, index));
            //Console.WriteLine(PlaceFeatures.Slice(startIndex, index).GetHashCode());
            //Console.WriteLine(new string(PlaceFeatures.Slice(startIndex, index).Span));
            //if (citiesMem.Contains(PlaceFeatures.Slice(startIndex, index)))
            //{
                var mySlice = featureSpan.Slice(0, index);
                mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = '~';
            //}
            featureSpan = featureSpan.Slice(index + 1);
            startIndex += index + 1;
        }

        return new string(words.Slice(0, count));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void DoSomething(ReadOnlyMemory<char> memory)
    {

    }

    public static string GetCityFromQueryMemory0(ReadOnlyMemory<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];
        int count = 0;

        while (PlaceFeatures.Length != 0)
        {
            int index = MyIndexOfAny(PlaceFeatures.Span);

            if (index == -1)
            {
                if (citiesMem.Contains(PlaceFeatures))
                {
                    PlaceFeatures.Span.CopyTo(words.Slice(count));
                    count += PlaceFeatures.Length;
                }
                break;
            }

            var mySlice = PlaceFeatures.Slice(0, index);
            if (citiesMem.Contains(mySlice))
            {
                mySlice.Span.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = '~';
            }
            PlaceFeatures = PlaceFeatures.Slice(index + 1);
        }

        return new string(words.Slice(0, count));
    }

    public static string GetCityFromQueryMemory1(ReadOnlyMemory<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];
        int count = 0;

        ReadOnlySpan<char> featureSpan = PlaceFeatures.Span;

        while (featureSpan.Length != 0)
        {
            int index = MyIndexOfAny(featureSpan);

            if (index == -1)
            {
                //if (citiesMem.Contains(PlaceFeatures))
                //{
                    featureSpan.CopyTo(words.Slice(count));
                    count += featureSpan.Length;
                //}
                break;
            }

            var mySlice = featureSpan.Slice(0, index);
            //if (citiesMem.Contains(PlaceFeatures.Slice(0, index)))
            //{
                mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = '~';
            //}
            //PlaceFeatures = PlaceFeatures.Slice(index + 1);
            featureSpan = featureSpan.Slice(index + 1);
        }

        return new string(words.Slice(0, count));
    }

    public static string GetCityFromQuerySpan(ReadOnlySpan<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];
        int count = 0;

        while (PlaceFeatures.Length != 0)
        {
            int index = MyIndexOfAny(PlaceFeatures);

            if (index == -1)
            {
                if (cities.Contains(new string(PlaceFeatures)))
                {
                    PlaceFeatures.CopyTo(words.Slice(count));
                    count += PlaceFeatures.Length;
                }
                break;
            }

            var mySlice = PlaceFeatures.Slice(0, index);
            if (cities.Contains(new string(mySlice)))
            {
                mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = '~';
            }
            PlaceFeatures = PlaceFeatures.Slice(index + 1);
        }

        return new string(words.Slice(0, count));
    }

    /*public static readonly IEqualityComparer<char> comparer = EqualityComparer<char>.Default;
    private const int Lower31BitMask = 0x7FFFFFFF;
    public bool Contains(ReadOnlySpan<char> item)
    {
        int hashCode = InternalGetHashCode(item);
        // see note at "HashSet" level describing why "- 1" appears in for loop
        for (int i = _buckets[hashCode % _buckets.Length] - 1; i >= 0; i = _slots[i].next)
        {
            if (_slots[i].hashCode == hashCode && comparer.Equals(_slots[i].value, item))
            {
                return true;
            }
        }
        return false;
    }

    public int GetHashCode(ReadOnlySpan<char> obj)
    {
        int h = 5381;

        foreach (char b in obj)
        {
            h = unchecked((h << 5) + h) ^ b.GetHashCode();
        }

        return h;
    }

    private int InternalGetHashCode(ReadOnlySpan<char> item)
    {
        return GetHashCode(item) & Lower31BitMask;
        //return comparer.GetHashCode(item) & Lower31BitMask;
    }*/

    public static string GetCityFromQuerySpan01(ReadOnlySpan<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        //Span<char> words = new char[PlaceFeatures.Length];

        StringBuilder str = new StringBuilder();

        int count = 0;

        while (PlaceFeatures.Length != 0)
        {
            int index = MyIndexOfAny(PlaceFeatures);

            if (index == -1)
            {
                string s = new string(PlaceFeatures);
                if (cities.Contains(s))
                {
                    str.Append(PlaceFeatures);
                    //str.Append(s);

                    //PlaceFeatures.CopyTo(words.Slice(count));
                    count += PlaceFeatures.Length;
                }
                break;
            }

            var mySlice = PlaceFeatures.Slice(0, index);
            string tempS = new string(mySlice);
            if (cities.Contains(tempS))
            {
                str.Append(mySlice);
                //str.Append(tempS);
                //mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                //words[count++] = '~';
                str.Append('~');
            }
            PlaceFeatures = PlaceFeatures.Slice(index + 1);
        }

        //return new string(words.Slice(0, count));
        return str.ToString();
    }

    public static string GetCityFromQuerySpan0(ReadOnlySpan<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];
        int count = 0;

        while (PlaceFeatures.Length != 0)
        {
            int index = MyIndexOfAny(PlaceFeatures);

            if (index == -1)
            {
                if (cities.Contains(new string(PlaceFeatures)))
                {
                    PlaceFeatures.CopyTo(words.Slice(count));
                    count += PlaceFeatures.Length;
                }
                break;
            }
            
            var mySlice = PlaceFeatures.Slice(0, index);
            if (cities.Contains(new string(mySlice)))
            {
                mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = '~';
            }
            PlaceFeatures = PlaceFeatures.Slice(index + 1);
        }

        return new string(words.Slice(0, count));
    }

    /*public static int MyIndexOfAny(ReadOnlyMemory<char> PlaceFeatures)
    {
        //Console.WriteLine(new string(PlaceFeatures));
        for (int i = 0; i < PlaceFeatures.Length; i++)
        {
            for (int j = 0; j < splitvalues.Length; j++)
            {
                if (splitvalues[j] == PlaceFeatures[i])
                {
                    return i;
                }
            }
        }
        return -1;
    }*/

    public static int MyIndexOfAny(ReadOnlySpan<char> PlaceFeatures)
    {
        //Console.WriteLine(new string(PlaceFeatures));
        for (int i = 0; i < PlaceFeatures.Length; i++)
        {
            for (int j = 0; j < splitvalues.Length; j++)
            {
                if (splitvalues[j] == PlaceFeatures[i])
                {
                    return i;
                }
            }
        }
        return -1;
    }

    public static string GetCityFromQuerySpan1(ReadOnlySpan<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        /*Span<char> temp1 = new char[1];
        temp1[0] = '~';

        Span<byte> temp2 = temp1.AsBytes();
        for(int i = 0; i < temp2.Length; i++)
        {
            Console.WriteLine("OK: " + i + " : " + temp2[i]);
        }*/
        


        /*Span<char> values = stackalloc char[5];
        values[0] = ' ';
        values[1] = ',';
        values[2] = '.';
        values[3] = '(';
        values[4] = ')';*/

        //Span<char> words = new char[PlaceFeatures.Length];
        Span<byte> words = new byte[PlaceFeatures.Length * 2];
        ReadOnlySpan<byte> featuresByte = PlaceFeatures.AsBytes();
        int count = 0;
        
        while (featuresByte.Length != 0)
        {
            //Console.WriteLine(featuresByte.Length);
            //int index = PlaceFeatures.IndexOfAny(splitvalues);
            //int index = PlaceFeatures.IndexOf('.');
            int index = MyIndexOfAny(featuresByte, splitvaluesBytes); //featuresByte.IndexOfAny(splitvaluesBytes);

            if (index == -1)
            {
                if (cities.Contains(new string(MemoryMarshal.Cast<byte, char>(featuresByte))))
                {
                    featuresByte.CopyTo(words.Slice(count));
                    count += featuresByte.Length;
                }
                break;
            }

            //index /= 2;
            var mySlice = featuresByte.Slice(0, index);
            //Console.WriteLine(new string(MemoryMarshal.Cast<byte, char>(mySlice)));
            //Console.WriteLine(new string(mySlice));
            //Console.WriteLine(mySlice.Length + " : " + index);
            if (cities.Contains(new string(MemoryMarshal.Cast<byte, char>(mySlice))))
            {
                mySlice.CopyTo(words.Slice(count));
                count += mySlice.Length;
                words[count++] = (byte)'~';
                count++;
            }
            featuresByte = featuresByte.Slice(index + 2);
        }

        return new string(MemoryMarshal.Cast<byte, char>(words.Slice(0, count)));
    }


    public static string GetCityFromQuerySpan2(ReadOnlySpan<char> PlaceFeatures)
    {
        if (PlaceFeatures.IsEmpty) return string.Empty;

        Span<char> words = new char[PlaceFeatures.Length];

        int index = -1;
        int count = 0;
        int startIndex = 0;
        while (startIndex < PlaceFeatures.Length)
        {
            index = PlaceFeatures.Slice(startIndex).IndexOf(' ');

            if (index == -1)
            {
                //if (cities.Contains(s))
                //{
                    //PlaceFeatures.Slice(startIndex).CopyTo(words.Slice(count));
                    count += PlaceFeatures.Length - startIndex;
                //}
                break;
            }
            else
            {
                //if (cities.Contains(s))
                //{
                    //PlaceFeatures.Slice(startIndex, index).CopyTo(words.Slice(count));
                    count += index;
                    words[count++] = '~';
                //}
            }
            startIndex += index + 1;

            //Console.WriteLine(startIndex + " : " + count);
        }

        return new string(words.Slice(0, count));
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MyIndexOfAny(ReadOnlySpan<byte> span, ReadOnlySpan<byte> values)
    {
        return IndexOfAny(
                ref MemoryMarshal.GetReference(span),
                span.Length,
                ref MemoryMarshal.GetReference(values),
                values.Length);
    }

    public static int IndexOfAny(ref byte searchSpace, int searchSpaceLength, ref byte value, int valueLength)
    {
        Debug.Assert(searchSpaceLength >= 0);
        Debug.Assert(valueLength >= 0);

        if (valueLength == 0)
            return 0;  // A zero-length sequence is always treated as "found" at the start of the search space.

        for (int i = 0; i < searchSpaceLength; i++)
        {
            if (IndexOf(ref value, Unsafe.Add(ref searchSpace, i), valueLength) != -1) return i;
        }
        return -1;
    }

    public static unsafe int IndexOf(ref byte searchSpace, byte value, int length)
    {
        Debug.Assert(length >= 0);

        uint uValue = value; // Use uint for comparisons to avoid unnecessary 8->32 extensions
        IntPtr index = (IntPtr)0; // Use UIntPtr for arithmetic to avoid unnecessary 64->32->64 truncations
        IntPtr nLength = (IntPtr)(uint)length;

        if (Vector.IsHardwareAccelerated && length >= Vector<byte>.Count * 2)
        {
            unchecked
            {
                int unaligned = (int)(byte*)Unsafe.AsPointer(ref searchSpace) & (Vector<byte>.Count - 1);
                nLength = (IntPtr)(uint)((Vector<byte>.Count - unaligned) & (Vector<byte>.Count - 1));
            }
        }
        SequentialScan:
        while ((byte*)nLength >= (byte*)8)
        {
            nLength -= 8;

            if (uValue == Unsafe.Add(ref searchSpace, index))
                goto Found;
            if (uValue == Unsafe.Add(ref searchSpace, index + 1))
                goto Found1;
            if (uValue == Unsafe.Add(ref searchSpace, index + 2))
                goto Found2;
            if (uValue == Unsafe.Add(ref searchSpace, index + 3))
                goto Found3;
            if (uValue == Unsafe.Add(ref searchSpace, index + 4))
                goto Found4;
            if (uValue == Unsafe.Add(ref searchSpace, index + 5))
                goto Found5;
            if (uValue == Unsafe.Add(ref searchSpace, index + 6))
                goto Found6;
            if (uValue == Unsafe.Add(ref searchSpace, index + 7))
                goto Found7;

            index += 8;
        }

        if ((byte*)nLength >= (byte*)4)
        {
            nLength -= 4;

            if (uValue == Unsafe.Add(ref searchSpace, index))
                goto Found;
            if (uValue == Unsafe.Add(ref searchSpace, index + 1))
                goto Found1;
            if (uValue == Unsafe.Add(ref searchSpace, index + 2))
                goto Found2;
            if (uValue == Unsafe.Add(ref searchSpace, index + 3))
                goto Found3;

            index += 4;
        }

        while ((byte*)nLength > (byte*)0)
        {
            nLength -= 1;

            if (uValue == Unsafe.Add(ref searchSpace, index))
                goto Found;

            index += 1;
        }

        if (Vector.IsHardwareAccelerated && ((int)(byte*)index < length))
        {
            nLength = (IntPtr)(uint)((length - (uint)index) & ~(Vector<byte>.Count - 1));
            // Get comparison Vector
            Vector<byte> vComparison = GetVector(value);
            while ((byte*)nLength > (byte*)index)
            {
                var vMatches = Vector.Equals(vComparison, Unsafe.ReadUnaligned<Vector<byte>>(ref Unsafe.AddByteOffset(ref searchSpace, index)));
                if (Vector<byte>.Zero.Equals(vMatches))
                {
                    index += Vector<byte>.Count;
                    continue;
                }
                // Find offset of first match
                return (int)(byte*)index + LocateFirstFoundByte(vMatches);
            }

            if ((int)(byte*)index < length)
            {
                unchecked
                {
                    nLength = (IntPtr)(length - (int)(byte*)index);
                }
                goto SequentialScan;
            }
        }

        return -1;
        Found: // Workaround for https://github.com/dotnet/coreclr/issues/13549
        return (int)(byte*)index;
        Found1:
        return (int)(byte*)(index + 1);
        Found2:
        return (int)(byte*)(index + 2);
        Found3:
        return (int)(byte*)(index + 3);
        Found4:
        return (int)(byte*)(index + 4);
        Found5:
        return (int)(byte*)(index + 5);
        Found6:
        return (int)(byte*)(index + 6);
        Found7:
        return (int)(byte*)(index + 7);
    }

    // Vector sub-search adapted from https://github.com/aspnet/KestrelHttpServer/pull/1138
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LocateFirstFoundByte(Vector<byte> match)
    {
        var vector64 = Vector.AsVectorUInt64(match);
        ulong candidate = 0;
        int i = 0;
        // Pattern unrolled by jit https://github.com/dotnet/coreclr/pull/8001
        for (; i < Vector<ulong>.Count; i++)
        {
            candidate = vector64[i];
            if (candidate != 0)
            {
                break;
            }
        }

        // Single LEA instruction with jitted const (using function result)
        return i * 8 + LocateFirstFoundByte(candidate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LocateFirstFoundByte(ulong match)
    {
        unchecked
        {
            // Flag least significant power of two bit
            var powerOfTwoFlag = match ^ (match - 1);
            // Shift all powers of two into the high byte and extract
            return (int)((powerOfTwoFlag * XorPowerOfTwoToHighByte) >> 57);
        }
    }

    private const ulong XorPowerOfTwoToHighByte = (0x07ul |
                                                       0x06ul << 8 |
                                                       0x05ul << 16 |
                                                       0x04ul << 24 |
                                                       0x03ul << 32 |
                                                       0x02ul << 40 |
                                                       0x01ul << 48) + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<byte> GetVector(byte vectorByte)
    {
        return new Vector<byte>(vectorByte);
    }
}
