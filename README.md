# AmcacheParser

See here for more info:

http://binaryforay.blogspot.com/2015/07/amcacheparser-reducing-noise-finding.html

    AmcacheParser version 0.0.5.1

    Author: Eric Zimmerman (saericzimmerman@gmail.com)
    https://github.com/EricZimmerman/AmcacheParser
    
            b               Path to file containing SHA-1 hashes to *include* from the results. Blacklisting overrides whitelisting
            f               Amcache.hve file to parse. Required
            i               Include file entries for Programs entries
            s               Directory where results will be saved. Required
            w               Path to file containing SHA-1 hashes to *exclude* from the results. Blacklisting overrides whitelisting
    
    Examples: AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -s C:\temp
              AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -i on -s C:\temp
              AmcacheParser.exe -f "C:\Temp\amcache\AmcacheWin10.hve" -w "c:\temp\whitelist.txt" -s C:\temp
              
              
This program is different from other amcache parsers in that it does not dump everything available. Rather, it looks at both file entries and program entries.

Program entries are found under 'Root\Programs' and File entries are found under 'Root\File'

AmcacheParser gathers information about all the Program entries, then looks at all the File entries. In each file entry is a pointer to a Program ID (value 100). If this program ID exists in Program entries, the file entry is associated with the Program entry.

At the end of this process you are left with things that didn't come from some kind of installed application.
              
Using the minimum options, AmcacheParser will only export Unassociated file entries.

If you use the -i option, AmcacheParser will export a Programs entry list and its associated File entry list.

With whitelisting and blacklisting you can further reduce the output by excluding certain file entries (whitelisting) or including only file entries whose SHA-1 is found in a blacklist file.

Note that AmcacheParser strips the extra 0s from the front of the SHA-1 value, so you can use the SHA-1s as shown in the output files for generating whitelists or blacklists.

Open Source Development funding and support provided by the following contributors: [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
