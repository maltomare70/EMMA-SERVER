-- =====================================================================
-- EMMA - Ricerca ibrida: ramo lessicale accanto a quello vettoriale
--
-- Da eseguire UNA VOLTA sul database emma, DOPO sql/rag.sql:
--
--   psql "$CONN" -f sql/rag_fulltext.sql
--
-- Se questa migrazione non viene eseguita, mettere Rag:HybridSearch = false
-- in appsettings: la ricerca resta solo vettoriale e non tocca la colonna tsv.
-- =====================================================================

-- Colonna generata: si mantiene da sola a ogni INSERT o UPDATE del contenuto,
-- quindi non c'e' nulla da aggiornare lato applicativo.
-- Il dizionario 'italian' porta stemming e stopword della lingua: "caricamento",
-- "caricare" e "carico" finiscono sulla stessa radice.
ALTER TABLE rag_chunks
    ADD COLUMN IF NOT EXISTS tsv tsvector
    GENERATED ALWAYS AS (to_tsvector('italian', contenuto)) STORED;

CREATE INDEX IF NOT EXISTS ix_rag_chunks_tsv
    ON rag_chunks USING gin (tsv);
