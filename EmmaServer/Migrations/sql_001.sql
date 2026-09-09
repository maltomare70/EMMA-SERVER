-- =====================================================================
-- EMMA - Database vettoriale per la ricerca semantica sui PDF
-- Da eseguire UNA VOLTA sul database emma (pgAdmin / psql / Render shell)
--
--   psql "$CONN" -f sql/rag.sql
--
-- Dimensione dell'embedding: 768 (Gemini text-embedding-004).
-- Se in futuro EMMA-AI cambia modello con una dimensione diversa,
-- va rifatta la colonna embedding e reindicizzati i documenti.
-- =====================================================================

-- 1. Estensione pgvector -----------------------------------------------
--    Su Render l'estensione e' disponibile ma va abilitata sul database.
CREATE EXTENSION IF NOT EXISTS vector;

-- 2. Anagrafica dei documenti indicizzati ------------------------------
CREATE TABLE IF NOT EXISTS rag_documents (
    id              SERIAL PRIMARY KEY,
    tenant          VARCHAR(100)  NOT NULL,
    file_name       VARCHAR(500)  NOT NULL,
    file_hash       VARCHAR(64)   NOT NULL,          -- SHA-256 del PDF: evita di reindicizzare lo stesso file
    file_size       BIGINT        NOT NULL DEFAULT 0,
    pagine          INT           NOT NULL DEFAULT 0,
    chunk_count     INT           NOT NULL DEFAULT 0,
    chunk_size      INT           NOT NULL DEFAULT 512,
    chunk_overlap   INT           NOT NULL DEFAULT 64,
    embedding_model VARCHAR(200)  NOT NULL DEFAULT '',
    embedding_dim   INT           NOT NULL DEFAULT 768,
    stato           INT           NOT NULL DEFAULT 0,  -- 0 = in corso, 1 = indicizzato, -1 = errore
    messaggio       VARCHAR(1000) NULL,
    allegato        BYTEA         NULL,                -- PDF originale
    data_creazione  TIMESTAMP     DEFAULT CURRENT_TIMESTAMP
);

-- Lo stesso file caricato due volte dallo stesso tenant e' un solo documento
CREATE UNIQUE INDEX IF NOT EXISTS uq_rag_documents_tenant_hash
    ON rag_documents (tenant, file_hash);

CREATE INDEX IF NOT EXISTS ix_rag_documents_tenant
    ON rag_documents (tenant);

-- 3. Chunk + vettori ---------------------------------------------------
CREATE TABLE IF NOT EXISTS rag_chunks (
    id             SERIAL PRIMARY KEY,
    id_doc         INT          NOT NULL REFERENCES rag_documents (id) ON DELETE CASCADE,
    tenant         VARCHAR(100) NOT NULL,
    chunk_index    INT          NOT NULL,             -- posizione del chunk nel documento
    pagina         INT          NOT NULL DEFAULT 0,   -- pagina del PDF da cui inizia il chunk
    token_count    INT          NOT NULL DEFAULT 0,
    contenuto      TEXT         NOT NULL,
    embedding      vector(768)  NOT NULL,
    data_creazione TIMESTAMP    DEFAULT CURRENT_TIMESTAMP,

    -- Ramo lessicale della ricerca ibrida: colonna generata, si mantiene da sola.
    -- Il dizionario 'italian' porta stemming e stopword della lingua.
    tsv tsvector GENERATED ALWAYS AS (to_tsvector('italian', contenuto)) STORED
);

CREATE INDEX IF NOT EXISTS ix_rag_chunks_doc
    ON rag_chunks (id_doc);

CREATE INDEX IF NOT EXISTS ix_rag_chunks_tenant
    ON rag_chunks (tenant);

CREATE INDEX IF NOT EXISTS ix_rag_chunks_tsv
    ON rag_chunks USING gin (tsv);

-- 4. Indice vettoriale: coseno + HNSW ----------------------------------
--    <=> e' l'operatore di distanza coseno; similarita' = 1 - distanza.
CREATE INDEX IF NOT EXISTS ix_rag_chunks_embedding
    ON rag_chunks USING hnsw (embedding vector_cosine_ops);

-- Parametro di ricerca consigliato (per sessione, non persistente):
--   SET hnsw.ef_search = 60;
