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
