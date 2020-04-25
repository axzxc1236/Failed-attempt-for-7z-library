## What is this?

A library that's supposed to parse header of .7z file, but in it's current state it won't work.

In the developement of Free mount I needed something to be able to extract part of file that's compressed in a 7z file  
(e.g archive.zip includes pi.txt that contains 100,000,000 digits of pi, I want something that can decode 6,864,325 th to 6,900,000 th of pi digit, efficiently  
 The current "best" option I have is unfortunately to decompress whole 100,000,000 digits of pi first)  
, so I started learning 7z format, then check if I can fingure out the thing I need.

## Rant

There is lots of missing puzzle, 7zFormat.txt alone is IMPOSSIBLE to make you understand 7z's header.  
eg: line 328 of 7zFormat.txt there is `  UINT64 UnPackSizes[]`, what is the size of "UnPackSizes"?  
    of line 344 `Digests[Number of streams with unknown CRC]`  
Then you will have to read the source code that comes with 7-zip source code or LZMA SDK.  
I tried, I'm bored, I'm tired, now I just want to give up.