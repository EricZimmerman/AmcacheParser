# AmcacheParser

## Command Line Interface
    
    AmcacheParser version 1.4.0.0
    
    Author: Eric Zimmerman (saericzimmerman@gmail.com)
    https://github.com/EricZimmerman/AmcacheParser
    
            b               Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting
            f               Amcache.hve file to parse. Required
            i               Include file entries for Programs entries
            w               Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting
    
            csv             Directory where CSV results will be saved to. Required
            csvf            File name to save CSV formatted results to. When present, overrides default name
    
            dt              The custom date/time format to use when displaying timestamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss
            mp              When true, display higher precision for timestamps. Default is FALSE
            nl              When true, ignore transaction log files for dirty hives. Default is FALSE
    
            debug           Show debug information during processing
            trace           Show trace information during processing
    
    
    Examples: AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" --csv C:\temp
              AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -i on --csv C:\temp --csvf foo.csv
              AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -w "c:\temp\whitelist.txt" --csv C:\temp
    
              Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes
                            
## Documentation

See here for more info: http://binaryforay.blogspot.com/2015/07/amcacheparser-reducing-noise-finding.html

This program is different from other Amcache parsers in that it does not dump everything available. Rather, it looks at both File entries and Program entries.

Program entries are found under `Root\Programs` and File entries are found under `Root\File`.

AmcacheParser gathers information about all the Program entries, then looks at all the File entries. In each file entry is a pointer to a Program ID (value 100). If this Program ID exists in Program entries, the File entry is associated with the Program entry.

At the end of this process you are left with things that didn't come from some kind of installed application.
              
Using the minimum options, AmcacheParser will only export Unassociated file entries.

If you use the `-i` option, AmcacheParser will export a Programs entry list and its associated File entry list.

With whitelisting and blacklisting you can further reduce the output by excluding certain File entries (whitelisting) or including only File entries whose SHA-1 is found in a blacklist file.

Note that AmcacheParser strips the extra 0s from the front of the SHA-1 value, so you can use the SHA-1s as shown in the output files for generating whitelists or blacklists.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). Use the [Get-ZimmermanTools](https://f001.backblazeb2.com/file/EricZimmermanTools/Get-ZimmermanTools.zip) PowerShell script to automate the download and updating of the EZ Tools suite. Additionally, you can automate each of these tools using [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape)!

# Special Thanks

Open Source Development funding and support provided by the following contributors: [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
