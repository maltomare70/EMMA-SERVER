-- =====================================================================
-- EMMA - Modulo Anomalie prezzi/quantita' (docs/modulo-anomalie.md, par. 6)
--
-- Eseguita da EmmaRepository.InitializeAsync dopo sql_001 e sql_002.
-- Idempotente: si puo' rilanciare.
--
-- Qui ci sono solo le tabelle che servono alle fasi 1-2 (avvisi + baseline).
-- ml_models arriva con la fase 3.
-- =====================================================================

-- 1. Avvisi -------------------------------------------------------------
--    stato + utente_feedback SONO il training set del filtro AutoML (fase 4):
--    vanno raccolti dal primo giorno, a posteriori non si recuperano.
CREATE TABLE IF NOT EXISTS anomalie (
    id              bigserial PRIMARY KEY,
    tenant          varchar(100) NOT NULL,
    doc_id          int NOT NULL REFERENCES docs(id) ON DELETE CASCADE,
    id_master       varchar(100),
    id_riga         varchar(100),
    id_fornitore    int,
    codice          varchar(256),
    tipo            varchar(40)  NOT NULL,   -- prezzo_alto | prezzo_basso | qta_anomala
                                             -- um_cambiata | incoerenza_totali | duplicato
                                             -- riga_anomala | nuovo_listino
                                             -- livello 1: riga_incoerente | valore_impossibile
                                             -- aliquota_iva | data_anomala | righe_gemelle
    livello         smallint     NOT NULL,   -- 1 regola, 2 statistica, 3 modello
    score           double precision,        -- z robusto oppure punteggio del modello
    severita        smallint     NOT NULL,   -- 0 ombra (invisibile), 1..3 visibile
    valore          numeric,
    atteso          numeric,
    delta_perc      numeric,
    messaggio       text,                    -- frase pronta, in italiano, per l'operatore
    modello_ver     varchar(40),             -- quale versione del rilevatore l'ha emessa
    stato           smallint DEFAULT 0,      -- 0 aperta, 1 confermata, 2 ignorata
    utente_feedback varchar(100),
    data_feedback   timestamptz,
    data_creazione  timestamptz DEFAULT now()
);

-- Nota dell'operatore con il feedback (aggiunta dopo la prima versione della tabella).
ALTER TABLE anomalie ADD COLUMN IF NOT EXISTS note text;

CREATE INDEX IF NOT EXISTS ix_anomalie_tenant_stato
    ON anomalie (tenant, stato, severita DESC, data_creazione DESC);

CREATE INDEX IF NOT EXISTS ix_anomalie_doc
    ON anomalie (doc_id);

-- 2. Baseline materializzata di notte -----------------------------------
--    Una riga per (tenant, fornitore, articolo, tipo documento): DDT ('2') e
--    fatture elettroniche ('4') non si mescolano, il prezzo non e' la stessa grandezza.
--
--    mediana_prezzo / mad_prezzo : scala lineare, euro per unita' di um_prevalente
--    mediana_qta    / mad_qta    : scala LOGARITMICA, cioe' mediana e MAD di ln(quantita)
--    n                           : osservazioni con l'unita' di misura prevalente
CREATE TABLE IF NOT EXISTS anomalie_baseline (
    id              bigserial PRIMARY KEY,
    tenant          varchar(100) NOT NULL,
    id_fornitore    int          NOT NULL,
    codice          varchar(256) NOT NULL,
    tipo_doc        smallint     NOT NULL,
    n               int          NOT NULL,
    mediana_prezzo  numeric, mad_prezzo numeric,
    mediana_qta     numeric, mad_qta    numeric,
    um_prevalente   varchar(20),
    primo_visto     date, ultimo_visto date,
    aggiornata_il   timestamptz DEFAULT now(),
    UNIQUE (tenant, id_fornitore, codice, tipo_doc)
);
