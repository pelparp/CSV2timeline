using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IniParser;
using IniParser.Model;
using NLog.Targets;
using NLog;
using CsvHelper;


class Program
{
    public static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();
    static void Main(string[] args)
    {

        Console.WriteLine("============================================================================");
        Console.WriteLine("                     /'__`\\ ( )_  _                   (_ )  _               ");
        Console.WriteLine("   ___   ___  _   _ (_)  ) )| ,_)(_)  ___ ___     __   | | (_)  ___     __  ");
        Console.WriteLine(" /'___)/',__)( ) ( )   /' / | |  | |/' _ ` _ `\\ /'__`\\ | | | |/' _ `\\ /'__`\\");
        Console.WriteLine("( (___ \\__, \\| \\_/ | /' /( )| |_ | || ( ) ( ) |(  ___/ | | | || ( ) |(  ___/");
        Console.WriteLine("`\\____)(____/`\\___/'(_____/'`\\__)(_)(_) (_) (_)`\\____)(___)(_)(_) (_)`\\____)");
        Console.WriteLine("============================================================================");
        string inputDirectory = null;
        string outputDirectory = null;
        string sourceSystem = null;

        if (args.Length == 0)
        {
            ShowHelp();
            return;
        }
        // Parse command line arguments
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-i" && i + 1 < args.Length)
            {
                inputDirectory = args[i + 1];
            }
            else if (args[i] == "-o" && i + 1 < args.Length)
            {
                outputDirectory = args[i + 1];
            }
            else if (args[i] == "-s" && i + 1 < args.Length)
            {
                sourceSystem = args[i + 1];
            }
        }

        /* Set up logging configuration */
        var logconfig = new NLog.Config.LoggingConfiguration();
        var logfile = new NLog.Targets.FileTarget("logfile")
        {
            Layout = @"[${longdate}] [${level}] [${callsite}:${callsite-linenumber}] ${message} ${exception}",
            FileName = outputDirectory + "\\CSV2Timeline_" + DateTime.UtcNow.ToFileTimeUtc() + ".log"
        };

        var logconsole = new ColoredConsoleTarget("logconsole") { Layout = @"[${longdate}] [${level}] ${message} ${exception}" };
        var lognullstream = new NLog.Targets.NullTarget();
        logconfig.AddRule(LogLevel.Info, LogLevel.Fatal, logfile, "Program*");
        logconfig.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole, "Program*");
        NLog.LogManager.Configuration = logconfig;
        _log.Info("CSV2Timeline has begun with input directory set to {inputDirectory} and output will be written to {outputDirectory}. Currently processing files for system name {systemName}",inputDirectory.ToString(),outputDirectory.ToString(),sourceSystem.ToString());

        if (string.IsNullOrEmpty(inputDirectory) || string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(sourceSystem))
        {
            ShowHelp();
            return;
        }

        // Read configurations
       
        var configs = ReadConfigurations("config.ini");

        // Process CSV files
        foreach (var file in Directory.GetFiles(inputDirectory, "*.csv"))
        {
            _log.Info("Reading file {file}", file);
            var config = GetMatchingConfiguration(file, configs);
            if (config != null)
            {
                _log.Info($"Found matching configuration {config.Name} for {file}");
                var timeline = ProcessCSV(file, config, sourceSystem);
                AppendTimelineToCSV(outputDirectory, timeline, config.TimestampDescription);
            }
            else
            {
                _log.Warn($"No matching configuration found for file: {file}");
            }
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("Usage: csv2timeline.exe -i <input_directory> -o <output_directory> -s <source_system>");
    }

    static List<Configuration> ReadConfigurations(string configFile)
    {
        var parser = new FileIniDataParser();
        var data = parser.ReadFile(configFile);

        List<Configuration> configs = new List<Configuration>();

        foreach (var section in data.Sections)
        {
            var config = new Configuration();
            config.Name = section.SectionName;
            config.DateTimeField = data[section.SectionName]["datetime"];
            config.Headers = data[section.SectionName]["headers"].Split(',');
            config.TimestampDescription = data[section.SectionName]["timestamp_desc"];
            config.MessageFormat = data[section.SectionName]["message"];
            config.Source = data[section.SectionName]["source"];

            // Check if the 'filter' key exists in the current section
            if (data[section.SectionName].ContainsKey("filter"))
            {
                // Split the filter string into individual filters
                config.Filters = data[section.SectionName]["filter"].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                // If 'filter' key is not present, set Filters to an empty array
                config.Filters = new string[0];
            }

            configs.Add(config);
        }

        return configs;
    }


    static Configuration GetMatchingConfiguration(string csvFile, List<Configuration> configs)
    {
        using (var reader = new StreamReader(csvFile))
        using (var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture))
        {
            csv.Read();
            csv.ReadHeader();

            var headers = csv.HeaderRecord;

            int maxMatchingHeaders = 0;
            Configuration matchingConfig = null;

            foreach (var config in configs)
            {
                int matchingHeadersCount = CountMatchingHeaders(headers, config.Headers);
                if (matchingHeadersCount > maxMatchingHeaders)
                {
                    maxMatchingHeaders = matchingHeadersCount;
                    matchingConfig = config;
                }
            }

            return matchingConfig;
        }
    }

    static int CountMatchingHeaders(string[] csvHeaders, string[] configHeaders)
    {
        int matchingCount = 0;

        foreach (var csvHeader in csvHeaders)
        {
            if (configHeaders.Contains(csvHeader))
            {
                matchingCount++;
            }
        }

        return matchingCount;
    }


    static bool AreHeadersMatching(string[] csvHeaders, string[] configHeaders)
    {
        if (csvHeaders.Length != configHeaders.Length)
        {
            return false;
        }

        for (int i = 0; i < csvHeaders.Length; i++)
        {
            if (csvHeaders[i] != configHeaders[i])
            {
                return false;
            }
        }

        return true;
    }

    static List<Event> ProcessCSV(string csvFile, Configuration config, string sourceSystem)
    {
        List<Event> timeline = new List<Event>();

        using (var reader = new StreamReader(csvFile))
        using (var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture))
        {
            // Assume the first row contains headers
            csv.Read();
            csv.ReadHeader();

            while (csv.Read())
            {
                // Check if any filters are specified in the configuration
                if (config.Filters == null || config.Filters.Length == 0 || MatchesFilter(csv, config.Filters))
                {
                    try
                    {
                        // Read datetime field specified in config
                        DateTime timestamp = DateTime.Parse(csv.GetField(config.DateTimeField));

                        // Construct message based on config
                        string message = ConstructMessage(config.MessageFormat, csv, config.Headers);

                        timeline.Add(new Event { Timestamp = timestamp, Message = message, Source = config.Source, SourceSystem = sourceSystem });
                    }
                    catch (FormatException e)
                    {
                        _log.Error(e.Message);
                    }
                }
            }
        }

        return timeline;
    }




    static string ConstructMessage(string messageFormat, CsvReader csv, string[] headers)
    {
        string message = messageFormat;

        foreach (var header in headers)
        {
            string placeholder = "{" + header + "}";
            string value = csv.GetField(header);

            message = message.Replace(placeholder, value);
        }

        return message;
    }

    static void AppendTimelineToCSV(string outputDirectory, List<Event> timeline, string timestampDescription)
    {
        string outputFilePath = Path.Combine(outputDirectory, "timeline.csv");

        bool fileExists = File.Exists(outputFilePath);
        bool headerWritten = false;

        using (var writer = new StreamWriter(outputFilePath, true)) // Use FileMode.Append to append to the existing file
        using (var csv = new CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture))
        {
            // Check if the file exists and if it's empty
            if (!fileExists || new FileInfo(outputFilePath).Length == 0)
            {
                // Write header only if the file does not exist or is empty
                csv.WriteHeader<Event>();
                csv.NextRecord();
                headerWritten = true;
            }

            // Write records
            if (headerWritten) // Skip writing header again if it's already written
            {
                csv.WriteRecords(timeline.Select(ev => new { ev.Timestamp, ev.SourceSystem, ev.Message, TimestampDescription = timestampDescription, ev.Source }));
            }
            else
            {
                foreach (var ev in timeline)
                {
                    csv.WriteRecord(new { ev.Timestamp, ev.SourceSystem, ev.Message, TimestampDescription = timestampDescription, ev.Source });
                    csv.NextRecord();
                }
            }
        }
    }

    // Method to check if a CSV record matches any filter expression
    static bool MatchesFilter(CsvReader csv, string[] filters)
    {
        foreach (var filter in filters)
        {
            // Split filter expression into individual conditions
            var conditions = filter.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            // Check if any condition matches
            if (conditions.All(condition => EvaluateCondition(csv, condition)))
            {
                return true; // Return true if any condition matches
            }
        }

        return false; // Return false if no filter condition matches
    }

    static bool EvaluateCondition(CsvReader csv, string condition)
    {
        // Check for parentheses
        if (condition.Contains('('))
        {
            // Extract condition within parentheses
            var innerCondition = condition.Substring(condition.IndexOf('(') + 1, condition.IndexOf(')') - condition.IndexOf('(') - 1);
            return EvaluateInnerCondition(csv, innerCondition);
        }
        else
        {
            // Evaluate regular condition
            return EvaluateInnerCondition(csv, condition);
        }
    }

    static bool EvaluateInnerCondition(CsvReader csv, string condition)
    {
        // Check if the condition specifies a partial match with "~"
        bool isPartialMatch = condition.Contains("~");

        // Split condition into field name and value
        var parts = condition.Split(new[] { isPartialMatch ? "~" : "=" }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            throw new ArgumentException("Invalid filter expression: " + condition);
        }

        string fieldName = parts[0].Trim();
        string filterValue = parts[1].Trim();

        // Check if the field value matches the filter value
        string recordValue = csv.GetField(fieldName);

        if (isPartialMatch)
        {
            // Perform a partial match (contains) check
            return recordValue.IndexOf(filterValue, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        else
        {
            // Perform an exact match
            return string.Equals(recordValue, filterValue, StringComparison.OrdinalIgnoreCase);
        }
    }

}

class Configuration
{
    public string Name { get; set; }
    public string DateTimeField { get; set; }
    public string[] Headers { get; set; }
    public string TimestampDescription { get; set; }
    public string MessageFormat { get; set; }
    public string Source { get; set; }
    public string[] Filters { get; set; } // Add Filters property
}

class Event
{
    public DateTime Timestamp { get; set; }
    public string SourceSystem { get; set; }
    public string Message { get; set; }
    public string TimestampDescription { get; set; }
    public string Source { get; set; }
}
