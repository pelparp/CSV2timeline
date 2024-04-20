# CSV2Timeline

CSV2Timeline is a command-line tool that converts CSV files containing event data into a timeline format. It's particularly useful for converting log files, browsing history, or any other event-based data into a format suitable for timeline analysis.

## Features

- Convert CSV files into a timeline format
- Support for multiple input CSV files
- Customizable configuration via INI files
- Filtering of records based on specified criteria
- Flexible timestamp and message formatting
- Supports various source systems and event types

## Installation

To use CSV2Timeline, simply download the latest release from the Releases section of this GitHub repository. There's no installation required, as it's a standalone executable.

## Usage
CSV2Timeline.exe -i <input_directory> -o <output_directory> -s <source_system>

- `-i`: Path to the input directory containing CSV files.
- `-o`: Path to the output directory where the timeline CSV will be saved.
- `-s`: Source system identifier for the events (e.g., "DC01", "FS01", etc.).

## Configuration

CSV2Timeline uses INI files for configuration. It expects a config.ini file in the program directory. Each section in the INI file represents a different type of event data and contains the following properties:

- `datetime`: Field name containing the timestamp of the event.
- `headers`: Comma-separated list of headers in the CSV file.
- `timestamp_desc`: Description of the timestamp field (e.g., "Timestamp", "Event Time").
- `message`: Message format for each event, with placeholders for CSV field values.
- `source`: Source system or application generating the event.
- `filter`: Optional filter expressions for including or excluding specific records.

Example configuration:

```
[evtx-csv]
datetime=TimeCreated
headers=RecordNumber,EventRecordId,TimeCreated,EventId,Level,Provider,Channel
timestamp_desc=Event Time
message=Event {EventId} occurred at {TimeCreated} in channel {Channel}
source=Microsoft-Windows-EventLog
filter=EventId=4624;EventId=4625
```

