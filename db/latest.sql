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