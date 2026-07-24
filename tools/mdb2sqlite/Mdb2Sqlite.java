import com.healthmarketscience.jackcess.Column;
import com.healthmarketscience.jackcess.DataType;
import com.healthmarketscience.jackcess.Database;
import com.healthmarketscience.jackcess.DatabaseBuilder;
import com.healthmarketscience.jackcess.Row;
import com.healthmarketscience.jackcess.Table;

import java.io.File;
import java.math.BigDecimal;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.Statement;
import java.util.List;

/**
 * MMUD Explorer database converter: Access .mdb -> SQLite .db.
 *
 * Full-fidelity copy: every table, every column, every row. Column names are
 * preserved verbatim (quoted), so the C# data layer can address the exact
 * field names the VB6 code uses. Type mapping:
 *   BYTE/INT/LONG/BOOLEAN        -> INTEGER
 *   FLOAT/DOUBLE                 -> REAL
 *   MONEY (Currency)/NUMERIC     -> TEXT (exact decimal string; the C# layer
 *                                   parses to decimal - no float drift)
 *   TEXT/MEMO/GUID               -> TEXT
 *   BINARY/OLE                   -> BLOB
 *   SHORT_DATE_TIME              -> TEXT (ISO-8601)
 *
 * Usage: java -jar mme-mdb2sqlite.jar <input.mdb> <output.db>
 */
public final class Mdb2Sqlite {

    public static void main(String[] args) throws Exception {
        if (args.length != 2) {
            System.err.println("Usage: java -jar mme-mdb2sqlite.jar <input.mdb> <output.db>");
            System.exit(2);
        }
        File mdbFile = new File(args[0]);
        File dbFile = new File(args[1]);
        if (dbFile.exists() && !dbFile.delete()) {
            System.err.println("Cannot overwrite " + dbFile);
            System.exit(2);
        }

        Class.forName("org.sqlite.JDBC");
        long t0 = System.currentTimeMillis();
        try (Database mdb = DatabaseBuilder.open(mdbFile);
             Connection sq = DriverManager.getConnection("jdbc:sqlite:" + dbFile.getPath())) {

            try (Statement st = sq.createStatement()) {
                st.execute("PRAGMA journal_mode=OFF");
                st.execute("PRAGMA synchronous=OFF");
            }
            sq.setAutoCommit(false);

            long totalRows = 0;
            for (String tableName : mdb.getTableNames()) {
                Table table = mdb.getTable(tableName);
                List<? extends Column> cols = table.getColumns();

                StringBuilder create = new StringBuilder("CREATE TABLE \"")
                        .append(tableName).append("\" (");
                StringBuilder insert = new StringBuilder("INSERT INTO \"")
                        .append(tableName).append("\" VALUES (");
                for (int i = 0; i < cols.size(); i++) {
                    if (i > 0) { create.append(", "); insert.append(", "); }
                    create.append('"').append(cols.get(i).getName()).append("\" ")
                          .append(sqliteType(cols.get(i).getType()));
                    insert.append('?');
                }
                create.append(')');
                insert.append(')');

                try (Statement st = sq.createStatement()) {
                    st.execute(create.toString());
                }

                long rows = 0;
                try (PreparedStatement ps = sq.prepareStatement(insert.toString())) {
                    for (Row row : table) {
                        for (int i = 0; i < cols.size(); i++) {
                            Column c = cols.get(i);
                            Object v = row.get(c.getName());
                            ps.setObject(i + 1, convert(c.getType(), v));
                        }
                        ps.addBatch();
                        if (++rows % 5000 == 0) ps.executeBatch();
                    }
                    ps.executeBatch();
                }
                sq.commit();
                totalRows += rows;
                System.out.printf("  %-12s %,8d rows, %d cols%n", tableName, rows, cols.size());
            }

            try (Statement st = sq.createStatement()) {
                // the lookup index every VB6 Seek("=", nNum) relies on
                for (String t : new String[]{"Items", "Monsters", "Spells",
                        "Classes", "Races", "Shops", "TBInfo", "Lairs"}) {
                    try {
                        st.execute("CREATE INDEX \"ix_" + t + "_Number\" ON \""
                                + t + "\" (\"Number\")");
                    } catch (Exception ignored) { /* table/column may not exist */ }
                }
                try {
                    st.execute("CREATE INDEX \"ix_Rooms_MapRoom\" ON \"Rooms\" "
                            + "(\"Map Number\", \"Room Number\")");
                } catch (Exception ignored) { }
            }
            sq.commit();

            System.out.printf("Done: %,d rows in %.1fs -> %s%n",
                    totalRows, (System.currentTimeMillis() - t0) / 1000.0, dbFile);
        }
    }

    private static String sqliteType(DataType t) {
        switch (t) {
            case BYTE: case INT: case LONG: case BOOLEAN: case BIG_INT:
                return "INTEGER";
            case FLOAT: case DOUBLE:
                return "REAL";
            case MONEY: case NUMERIC:
                return "TEXT"; // exact decimal string
            case BINARY: case OLE:
                return "BLOB";
            default:
                return "TEXT";
        }
    }

    private static Object convert(DataType t, Object v) {
        if (v == null) return null;
        switch (t) {
            case BOOLEAN:
                return ((Boolean) v) ? -1 : 0; // VB6 True = -1
            case MONEY: case NUMERIC:
                return ((BigDecimal) v).stripTrailingZeros().toPlainString();
            case SHORT_DATE_TIME:
                return v.toString();
            default:
                return v;
        }
    }
}
