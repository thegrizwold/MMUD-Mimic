# mme-mdb2sqlite

One-shot converter: mmud Access database (.mdb) -> SQLite (.db) for the
MMUD Explorer C# application.

## Usage

    java -jar mme-mdb2sqlite.jar <input.mdb> <output.db>

Example:

    java -jar mme-mdb2sqlite.jar data-v1.11p.mdb mmud-1.11p.db

Requires any Java 11+ runtime. The jar is self-contained (Jackcess 4.0.5,
sqlite-jdbc, slf4j-nop bundled).

## What it does

- Copies every table, column, and row. Column names preserved verbatim so
  the C# data layer addresses the exact fields the VB6 code used.
- Type mapping: BYTE/INT/LONG/BOOLEAN -> INTEGER (VB6 True = -1);
  FLOAT/DOUBLE -> REAL; MONEY/NUMERIC (Currency) -> TEXT as exact decimal
  strings (no float drift); dates -> ISO text; BINARY/OLE -> BLOB.
- Creates Number indexes on the seek tables (Items, Monsters, Spells,
  Classes, Races, Shops, TBInfo, Lairs) and (Map Number, Room Number)
  on Rooms - the SQLite equivalents of the Jet indexes the VB6 Seek
  calls relied on.

## Rebuilding the jar

    cd tools/mdb2sqlite
    # fetch deps into lib/ (jackcess 4.0.5, commons-lang3 3.12, 
    # commons-logging 1.2, sqlite-jdbc 3.45.1, slf4j-api+nop 2.0.12)
    javac -cp "lib/*" Mdb2Sqlite.java
    # merge classes + dep jars into a fat jar with Main-Class: Mdb2Sqlite
