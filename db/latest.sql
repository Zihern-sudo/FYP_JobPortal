ALTER TABLE `user`
MODIFY `user_status` ENUM('Active', 'Suspended', 'Inactive') NOT NULL DEFAULT 'Active';

/* allow for inactive */
/* 7:44pm */
ALTER TABLE job_application
ADD resume_path NVARCHAR(255) NULL; /* add reusme path */

ALTER TABLE job_listing
ADD job_category NVARCHAR(50) NOT NULL DEFAULT 'Full Time',
ADD work_mode NVARCHAR(50) NOT NULL DEFAULT 'On-site',
ADD CONSTRAINT chk_job_category CHECK (job_category IN ('Contract','Freelance','Full Time','Internship','Part Time')),
ADD CONSTRAINT chk_work_mode CHECK (work_mode IN ('On-site','WFH','Hybrid'));
/* added to do for filters 1/11/2025 7:44pm */

use jobportal;
ALTER TABLE user
ADD COLUMN phone VARCHAR(30) NULL,
ADD COLUMN address TEXT NULL,
ADD COLUMN work_experience TEXT NULL,
ADD COLUMN education TEXT NULL,
ADD COLUMN skills TEXT NULL,
ADD COLUMN notif_inapp BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_email BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_sms BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_job_updates BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_feedback BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_messages BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_system BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_reminders BOOLEAN NOT NULL DEFAULT FALSE;

/* 10:27pm 2/11/2025 updated user table to save settings */



/* 3:11pm 3/11/2025 updated job lisitng table with new expiry_date and job_requirements_nice col */
/*Disable Safe Updates*/

-- 1) Ensure new column exists (idempotent)
SET @sql := IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'job_listing'
      AND COLUMN_NAME = 'job_requirements_nice') = 0,
  'ALTER TABLE `job_listing` ADD COLUMN `job_requirements_nice` TEXT NULL AFTER `job_requirements`;',
  'SELECT "job_requirements_nice already exists" AS info;'
);
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- (optional) deadline column used by code
SET @sql := IF(
  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'job_listing'
      AND COLUMN_NAME = 'expiry_date') = 0,
  'ALTER TABLE `job_listing` ADD COLUMN `expiry_date` DATETIME NULL AFTER `date_posted`;',
  'SELECT "expiry_date already exists" AS info;'
);
PREPARE s2 FROM @sql; EXECUTE s2; DEALLOCATE PREPARE s2;

-- 2) Migrate legacy combined data:
--    We split on the token "||NICE||" (works with or without newlines around it)
SET @delim := '||NICE||';

UPDATE `job_listing`
SET
  `job_requirements_nice` = CASE
    WHEN LOCATE(@delim, `job_requirements`) > 0
      THEN NULLIF(
             TRIM(REPLACE(REPLACE(SUBSTRING_INDEX(`job_requirements`, @delim, -1), '\r',''), '\n','')),
             ''
           )
    ELSE `job_requirements_nice`
  END,
  `job_requirements` = TRIM(
     REPLACE(REPLACE(
       CASE
         WHEN LOCATE(@delim, `job_requirements`) > 0
           THEN SUBSTRING_INDEX(`job_requirements`, @delim, 1)
         ELSE `job_requirements`
       END
     , '\r',''), '\n','')
  );

-- 3) Enforce separation going forward: forbid the delimiter in job_requirements
--    (prevents anyone from packing must/nice in one column again)
--    If this fails because constraint exists already, you can ignore.
ALTER TABLE `job_listing`
  ADD CONSTRAINT `chk_job_req_no_nice_delim`
  CHECK (`job_requirements` IS NULL OR INSTR(`job_requirements`, '||NICE||') = 0);

/* ---------------------------------------------------------------------------------------------- */


/* 8.09pm 3/11/2025 updated 1 Recruiter 1 Company */

/* ================================================================
   PRE-CHECKS (read-only)
   ================================================================ */

-- No recruiters should have more than one company (expect >0 before fix)
SELECT user_id, COUNT(*) AS company_count
FROM `company`
GROUP BY user_id
HAVING company_count > 1;

-- company_id range and count (avoid reserved word)
SELECT MIN(company_id) AS min_id,
       MAX(company_id) AS max_id,
       COUNT(*)        AS row_count
FROM `company`;

-- Each recruiter’s jobs: distinct companies referenced
SELECT jl.user_id,
       COUNT(DISTINCT jl.company_id) AS distinct_companies_for_user,
       COUNT(*) AS total_jobs
FROM `job_listing` jl
GROUP BY jl.user_id
ORDER BY distinct_companies_for_user DESC;

-- Optional FK sanity: any job pointing to a non-existent company? (should be 0)
SELECT COUNT(*) AS orphans
FROM `job_listing` jl
LEFT JOIN `company` c ON c.company_id = jl.company_id
WHERE c.company_id IS NULL;


/* =====================================================================
   MAIN: Enforce 1 Recruiter → 1 Company + Delete Extras + Renumber IDs
   Renumbering order: (user_id, company_name, company_id)
   MySQL 8+  |  Tables: company, job_listing
   ===================================================================== */

START TRANSACTION;

-- If you previously used triggers, remove them. We'll enforce via unique index.
DROP TRIGGER IF EXISTS trg_company_no_dup_before_insert;
DROP TRIGGER IF EXISTS trg_company_no_dup_before_update;

-- 1) Usage per company
DROP TEMPORARY TABLE IF EXISTS tmp_company_usage;
CREATE TEMPORARY TABLE tmp_company_usage AS
SELECT
  c.user_id,
  c.company_id,
  COALESCE(j.cnt, 0) AS job_count
FROM `company` c
LEFT JOIN (
  SELECT company_id, COUNT(*) AS cnt
  FROM `job_listing`
  GROUP BY company_id
) j ON j.company_id = c.company_id;

-- 2) Primary company per recruiter: most jobs, tie → highest company_id
DROP TEMPORARY TABLE IF EXISTS tmp_primary_company;
CREATE TEMPORARY TABLE tmp_primary_company AS
SELECT user_id, company_id AS primary_company_id
FROM (
  SELECT
    user_id,
    company_id,
    job_count,
    ROW_NUMBER() OVER (PARTITION BY user_id ORDER BY job_count DESC, company_id DESC) AS rn
  FROM tmp_company_usage
) ranked
WHERE rn = 1;

-- 3) Repoint all jobs to the chosen primary
UPDATE `job_listing` jl
JOIN `company` c ON c.company_id = jl.company_id
JOIN tmp_primary_company p ON p.user_id = c.user_id
SET jl.company_id = p.primary_company_id
WHERE jl.company_id <> p.primary_company_id;

-- 4) DELETE redundant companies (now unused by jobs)
DELETE c
FROM `company` c
JOIN tmp_primary_company p ON p.user_id = c.user_id
WHERE c.company_id <> p.primary_company_id;

-- 5) RENumber company_id to start from 1 (no gaps) and update references
SET @prev_fk_checks := @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;

-- Map old -> new ids AFTER deletions
-- ORDER BY user_id, company_name (NULLs first as empty string), then company_id
DROP TEMPORARY TABLE IF EXISTS tmp_company_map;
CREATE TEMPORARY TABLE tmp_company_map AS
SELECT
  c.company_id AS old_id,
  ROW_NUMBER() OVER (
    ORDER BY
      c.user_id ASC,
      COALESCE(c.company_name, '') ASC,
      c.company_id ASC
  ) AS new_id
FROM `company` c;

-- Renumbered copy
DROP TEMPORARY TABLE IF EXISTS company_renumbered;
CREATE TEMPORARY TABLE company_renumbered LIKE `company`;

INSERT INTO company_renumbered
  (company_id, user_id, company_name, company_industry, company_location, company_description, company_status)
SELECT
  m.new_id, c.user_id, c.company_name, c.company_industry, c.company_location, c.company_description, c.company_status
FROM `company` c
JOIN tmp_company_map m ON m.old_id = c.company_id
ORDER BY m.new_id;

-- Replace original table data
TRUNCATE TABLE `company`;
INSERT INTO `company`
  (company_id, user_id, company_name, company_industry, company_location, company_description, company_status)
SELECT
  company_id, user_id, company_name, company_industry, company_location, company_description, company_status
FROM company_renumbered
ORDER BY company_id;

-- Update FKs in job_listing
UPDATE `job_listing` jl
JOIN tmp_company_map m ON m.old_id = jl.company_id
SET jl.company_id = m.new_id;

-- Reset AUTO_INCREMENT to next value
SET @next_ai := (SELECT IFNULL(MAX(company_id),0) + 1 FROM `company`);
SET @sql := CONCAT('ALTER TABLE `company` AUTO_INCREMENT = ', @next_ai);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET FOREIGN_KEY_CHECKS = @prev_fk_checks;

-- 6) Enforce going forward: unique index on company(user_id)
-- Create only if not exists
SET @idx_exists := (
  SELECT COUNT(*)
  FROM INFORMATION_SCHEMA.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'company'
    AND INDEX_NAME = 'UX_company_user'
);
SET @sql := IF(@idx_exists = 0,
  'ALTER TABLE `company` ADD UNIQUE INDEX `UX_company_user` (`user_id`);',
  'DO 0;'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

COMMIT;


/* ================================================================
   POST-CHECKS (read-only)
   ================================================================ */

-- Should be zero offenders now
SELECT user_id, COUNT(*) AS company_count
FROM `company`
GROUP BY user_id
HAVING company_count > 1;

-- company_id range and count
SELECT MIN(company_id) AS min_id,
       MAX(company_id) AS max_id,
       COUNT(*)        AS row_count
FROM `company`;

-- Each recruiter’s jobs should point to exactly one company
SELECT jl.user_id,
       COUNT(DISTINCT jl.company_id) AS distinct_companies_for_user,
       COUNT(*) AS total_jobs
FROM `job_listing` jl
GROUP BY jl.user_id
ORDER BY distinct_companies_for_user DESC;

-- Orphan check (should be 0)
SELECT COUNT(*) AS orphans
FROM `job_listing` jl
LEFT JOIN `company` c ON c.company_id = jl.company_id
WHERE c.company_id IS NULL;



/* ---------------------------------------------------------------------------------------------- */

/* 4.04pm 4/11/2025 updated Company Status Length */

ALTER TABLE jobportal.company MODIFY company_status VARCHAR(20);

/* ---------------------------------------------------------------------------------------------- */

/* 5:16pm 6/11/2025 change user and notifpref tables */
ALTER TABLE user
DROP COLUMN notif_inapp,
DROP COLUMN notif_email,
DROP COLUMN notif_sms,
DROP COLUMN notif_job_updates,
DROP COLUMN notif_feedback,
DROP COLUMN notif_messages,
DROP COLUMN notif_system,
DROP COLUMN notif_reminders;

ALTER TABLE notification_preference
DROP COLUMN allow_SMS,
ADD COLUMN notif_job_updates BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_messages BOOLEAN NOT NULL DEFAULT FALSE,
ADD COLUMN notif_reminders BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE notification_preference 
CHANGE COLUMN `allow_inApp` `allow_inApp` TINYINT(1) NOT NULL DEFAULT '0' ;
CHANGE COLUMN `allow_email` `allow_email` TINYINT(1) NOT NULL DEFAULT '0' ;

ALTER TABLE user
ADD COLUMN profile_picture VARCHAR(255) NULL;
/* 5:16pm */

/* 6.03pm 7/11/2025 add company photo to company table */

ALTER TABLE `company`
  ADD COLUMN `company_photo` VARCHAR(255) NULL
  AFTER `company_description`;

/* ---------------------------------------------------------------------------------------------- */

/* 9:19pm 10/11/2025 zihern */
-- New table to track short-lived email verification links
CREATE TABLE IF NOT EXISTS email_verification (
  token_id       INT AUTO_INCREMENT PRIMARY KEY,
  email          VARCHAR(190) NOT NULL,
  token          CHAR(36)     NOT NULL,        -- GUID
  purpose        ENUM('RecruiterRegister') NOT NULL DEFAULT 'RecruiterRegister',
  expires_at     DATETIME     NOT NULL,
  used           TINYINT(1)   NOT NULL DEFAULT 0,
  created_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT uq_email_latest UNIQUE (email, token)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX idx_email_purpose ON email_verification (email, purpose);
CREATE INDEX idx_exp_used      ON email_verification (expires_at, used);

/* ------------------------------------ */

/* 2:42pm 11/11/2025 eason job_category to job_type SQL */
-- 1️⃣ Drop the existing CHECK constraint that references job_category
ALTER TABLE job_listing
DROP CHECK chk_job_category;

-- 2️⃣ Rename the column job_category to job_type
ALTER TABLE job_listing
CHANGE COLUMN job_category job_type VARCHAR(50) 
CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'Full Time';

-- 3️⃣ Recreate the CHECK constraint using the new column name
ALTER TABLE job_listing
ADD CONSTRAINT chk_job_type 
CHECK (job_type IN ('Contract','Freelance','Full Time','Internship','Part Time'));

/* --------------------------------*/