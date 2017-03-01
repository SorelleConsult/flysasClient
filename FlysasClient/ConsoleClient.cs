﻿using System;
using System.Collections.Generic;
using System.Linq;
using FlysasLib;

namespace FlysasClient
{
    public class ConsoleClient
    {
        SASRestClient client = new SASRestClient();
        Options options = new FlysasClient.Options();

        public ConsoleClient()
        {

        }

        public ConsoleClient(Options options)
        {
            this.options = options;
        }

        public void InputLoop()
        {

            string input = null;
            var parser = new Parser();
            while (input != "q")
            {
                Console.WriteLine("Syntax: Origin-Destination outDate [inDate]");
                Console.Write(">>");
                input = Console.ReadLine();
                if (!options.Parse(input) && !Command(input))
                    foreach (string query in input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        SASQuery req = null;
                        try
                        {
                            req = parser.Parse(query);
                        }
                        catch (ParserException ex)
                        {
                            Console.Write("Syntax error:" + ex.Message);
                        }
                        catch (Exception ex)
                        {
                            Console.Write("Syntax error:");
                        }
                        if (req != null)
                        {
                            var res = client.Search(req);
                            if (res.errors != null && res.errors.Any())
                                Console.WriteLine("flysas.com says: " + res.errors.First().errorMessage);
                            else
                            {
                                Console.WriteLine("*********Outbound*******");
                                PrintFlights(res.outboundFlights, res.outboundFlightProducts, options);
                                if (req.InDate.HasValue)
                                {
                                    Console.WriteLine("*********Inbound*******");
                                    PrintFlights(res.inboundFlights, res.inboundFlightProducts, options);
                                }
                            }
                        }
                        Console.Write(Environment.NewLine + Environment.NewLine);
                    }
            }
        }

        enum Commands
        {
            Login, History, Logout, Points, Set, Help, Benchmark, Options
        };

        HashSet<Commands> requiresLogin = new HashSet<Commands>() { Commands.History, Commands.Points };

        bool Command(string input)
        {

            var names = Enum.GetNames(typeof(Commands));
            var stack = new Stack<string>(input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Reverse());
            if (stack.Any())
            {
                var sCmd = stack.Pop();
                var name = names.FirstOrDefault(s => s.Equals(sCmd, StringComparison.CurrentCultureIgnoreCase));
                if (name == null)
                    return false;
                Commands cmd = (Commands)Enum.Parse(typeof(Commands), name);
                if (!client.LoggedIn && requiresLogin.Contains(cmd))
                {
                    Console.WriteLine("This feature requires login");
                    return true;
                }
                switch (cmd)
                {
                    case Commands.Login:
                        {
                            bool result;
                            try
                            {
                                result = client.Login(options.UserName, options.Password);
                                Console.WriteLine("Login : " + (result ? " success" : "failed"));
                            }
                            catch (MyHttpException ex)
                            {
                                Console.WriteLine("Login failed");
                            }
                        }
                        break;
                    case Commands.History:
                        {
                            int n = 1;
                            int pages = 1;
                            bool fetchAll = true;
                            if (stack.Any())
                            {
                                int.TryParse(stack.Pop(), out n);
                                fetchAll = false;
                            }
                            List<Transaction> all = new List<Transaction>();
                            TransactionRoot res = null;
                            Table t = new Table();
                            Console.WriteLine("");
                            do
                            {
                                Console.Write("\rFetching page " + n + (pages > 1 ? " of " + pages.ToString() : ""));
                                try
                                {
                                    res = client.History(n);
                                }
                                catch (MyHttpException ex)
                                {
                                    Console.WriteLine("Error getting page " + n);
                                    Console.WriteLine(ex.Message);
                                }
                                n++;
                                if (fetchAll)
                                    pages = res.eurobonus.transactionHistory.totalNumberOfPages;
                                if (res.errors == null && res.eurobonus != null && res.eurobonus.transactionHistory.transaction != null)
                                {
                                    all.AddRange(res.eurobonus.transactionHistory.transaction);

                                    foreach (var r in res.eurobonus.transactionHistory.transaction)
                                    {
                                        var values = new List<string>();
                                        values.Add(r.datePerformed.ToString("yyyy-MM-dd"));
                                        values.Add(r.typeOfTransaction);
                                        values.Add(r.basicPointsAfterTransaction);
                                        values.Add(r.availablePointsAfterTransaction.ToString());
                                        values.Add(r.description);
                                        t.Rows.Add(values);
                                    }
                                }
                            } while (n <= pages);
                            Console.SetCursorPosition(0, Console.CursorTop);
                            t.Print();
                            if (fetchAll)
                                foreach (var g in all.GroupBy(trans => trans.typeOfTransaction))
                                    Console.WriteLine(g.Key + "\t" + g.Sum(trans => trans.availablePointsAfterTransaction));
                        }
                        break;
                    case Commands.Points:
                        {
                            try
                            {
                                var res = client.History(1);
                                Console.WriteLine("Status: " + res.eurobonus.currentTierCode);
                                Console.WriteLine(res.eurobonus.totalPointsForUse + " points for use");
                                Console.WriteLine(res.eurobonus.pointsAvailable + " basic points earned this period");
                            }
                            catch (System.Net.Http.HttpRequestException ex)
                            {
                                Console.WriteLine("Error getting info");
                                Console.WriteLine(ex.Message);

                            }
                        }
                        break;
                    case Commands.Benchmark:
                        var count = 40;
                        int threads = 6;
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        System.Threading.Tasks.Parallel.For(0, count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads }, x =>
                        {
                            SASQuery q = new SASQuery { From = "KLR", To = "ARN", OutDate = DateTime.Now.AddDays(1 + x).Date };
                            var w2 = System.Diagnostics.Stopwatch.StartNew();
                            var res = client.Search(q);
                            Console.WriteLine("Got " + res.outboundFlights?.Count + " in " + w2.Elapsed.TotalSeconds);

                        });
                        Console.WriteLine(watch.Elapsed.TotalSeconds);
                        break;
                    case Commands.Options:
                        Console.Write(options.Help() + Environment.NewLine);
                        break;
                    case Commands.Help:
                        Console.WriteLine("Commands:");
                        foreach (var s in names)
                            Console.WriteLine("\t" + s);
                        break;
                }
                return true;
            }
            return false;
        }


        void PrintFlights(IEnumerable<FlightBaseClass> flights, IEnumerable<FlightProductBaseClass> products, FlysasClient.Options options)
        {
            string slash = "/";
            string format = "HH:mm";
            var codes = products.Select(p => p.productCode).Distinct().ToArray();
            var first = flights.First();
            var headers = new List<string>();
            headers.Add(first.origin.code);
            headers.Add(first.destination.code);
            if (options.OutputEquipment)
                headers.Add("Equip");
            foreach (var c in codes)
            {
                headers.Add(c);
                if (options.OutputBookingClass)
                    headers.Add("");
            }
            Table table = new Table();
            table.Rows.Add(headers);
            foreach (var r in flights)
            {
                var values = new List<string>();
                var prices = products.Where(p => p.id.EndsWith("_" + r.id.ToString())).ToArray();
                var dateDiff = (r.endTimeInLocal.Date - r.startTimeInGmt.Date).Days;
                values.Add(r.startTimeInLocal.ToString(format));
                values.Add(r.endTimeInLocal.ToString(format) + (dateDiff > 0 ? "+" + dateDiff : ""));
                if (options.OutputEquipment)
                {
                    var s = string.Join(slash, simplify(r.segments.Select(seg => seg.airCraft.code)));
                    values.Add(s);
                }
                foreach (var c in codes)
                {
                    string sClasses = "";
                    var p = prices.FirstOrDefault(price => price.productCode == c);
                    var sPrice = "";
                    if (p != null)
                    {

                        var classes = p.fares.Select(f => f.bookingClass + f.avlSeats);
                        if (classes.Distinct().Count() == 1)
                            sClasses = classes.First();
                        else
                        {
                            sClasses = string.Join("/", classes);
                            //if (sClasses.Length >= tabLen)
                            //    sClasses = string.Join("/", p.fares.Select(f => f.bookingClass));
                        }
                        sPrice = p.price.formattedTotalPrice;
                    }
                    values.Add(sPrice);
                    if (options.OutputBookingClass)
                        values.Add(sClasses);
                }
                table.Rows.Add(values);
            }
            if (options.Table)
                table.PrintTable();
            else
                table.Print();
        }

        public class Table
        {
            public List<List<string>> Rows { get; private set; } = new List<List<string>>();
            string tab = "\t";
            int tabLen = 8;
            Dictionary<int, int> dict = new Dictionary<int, int>();
            void calc()
            {
                if (Rows.Any())
                    for (int i = 0; i < Rows.First().Count; i++)
                        dict[i] = Rows.Select(r => r[i]).Select(s => s == null ? 0 : s.Length).Max();
            }

            public void Print()
            {
                calc();
                foreach (var r in Rows)
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        var s = r[i] ?? string.Empty;
                        var len = dict[i] + tabLen - dict[i] % tabLen;
                        var pad = (len - s.Length - 1) / tabLen;
                        Console.Write(s);
                        for (int j = 0; j <= pad; j++)
                            Console.Write(tab);
                    }
                    Console.Write(Environment.NewLine);
                }
            }
            public void PrintTable()
            {
                var pad = 2;
                calc();
                foreach (var r in Rows)
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        var s = r[i] ?? string.Empty;
                        var len = dict[i] + pad - 1;
                        Console.Write(s);
                        for (int j = s.Length; j < len; j++)
                            Console.Write(" ");
                        Console.Write("|");
                    }
                    Console.Write(Environment.NewLine);
                    foreach (int i in dict.Values)
                        for (int j = 0; j < i + pad; j++)
                            Console.Write("-");
                    Console.Write(Environment.NewLine);
                }
            }
        }

        private IEnumerable<string> simplify(IEnumerable<string> list)
        {
            if (list.Distinct().Count() == 1)
                yield return list.First();
            else
                foreach (string s in list)
                    yield return s;
        }
        private string limit(string s)
        {
            if (s != null && s.Length < 8)
                return s;
            return string.Empty;
        }
    }
}
