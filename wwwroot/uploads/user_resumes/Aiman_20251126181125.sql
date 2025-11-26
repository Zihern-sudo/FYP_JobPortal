USE jobportal;

-- -------------------------------------------------------
-- PART 1: USERS (20 records)  -- R2 distribution (2 Admin, 8 Recruiter, 10 JobSeekers)
-- -------------------------------------------------------
INSERT INTO `user` (first_name, last_name, email, password_hash, user_role, user_2FA, user_status)
VALUES
-- 2 Admins
('Mohamad','Iskandar','mohamad.iskandar@example.com','HASH','Admin',0,'Active'),
('Aminah','Zul','aminah.zul@example.com','HASH','Admin',0,'Active'),

-- 8 Recruiters (IDs 3..10)
('Farid','Hassan','farid.hassan@talentace.com','HASH','Recruiter',0,'Active'),
('Siti','Aminah','siti.aminah@codewave.com','HASH','Recruiter',0,'Active'),
('Rashid','BinAli','rashid.ali@techforge.com','HASH','Recruiter',0,'Active'),
('Nurul','Hidayah','nurul.hidayah@pixelhub.com','HASH','Recruiter',0,'Active'),
('Wong','Liang','wong.liang@novalabs.com','HASH','Recruiter',0,'Active'),
('Kumar','Ravichandran','kumar.r@recruitnow.com','HASH','Recruiter',0,'Active'),
('Lee','Chong','lee.chong@rocketforge.com','HASH','Recruiter',0,'Active'),
('Haslina','Kamaruddin','haslina.k@hrsmart.com','HASH','Recruiter',0,'Active'),

-- 10 JobSeekers (IDs 11..20)
('Hafiz','Rahman','hafiz.rahman@example.com','HASH','JobSeeker',0,'Active'),
('Aisyah','BintiSalim','aisyah.salim@example.com','HASH','JobSeeker',0,'Active'),
('Amira','Lee','amira.lee@example.com','HASH','JobSeeker',0,'Active'),
('Jason','Tan','jason.tan@example.com','HASH','JobSeeker',0,'Active'),
('Nur','Syafiqah','nur.syafiqah@example.com','HASH','JobSeeker',0,'Active'),
('Irfan','Kamil','irfan.kamil@example.com','HASH','JobSeeker',0,'Active'),
('Suresh','Nair','suresh.nair@example.com','HASH','JobSeeker',0,'Active'),
('Mei','Ling','mei.ling@example.com','HASH','JobSeeker',0,'Active'),
('Zulkifli','Abdullah','zulkifli.abdullah@example.com','HASH','JobSeeker',0,'Active'),
('Farhana','Aziz','farhana.aziz@example.com','HASH','JobSeeker',0,'Active');

-- -------------------------------------------------------
-- PART 1: COMPANIES (20 records)
-- Note: company.user_id must refer to a Recruiter user (IDs 3..10 above)
-- Columns: user_id, company_name, company_industry, company_location, company_description, company_status
-- -------------------------------------------------------
INSERT INTO `company` (user_id, company_name, company_industry, company_location, company_description, company_status)
VALUES
(3,'TalentAce Recruitment','Recruitment','Shah Alam','Regional recruitment & staffing services','Active'),
(3,'TalentAce TechHire','HR Tech','Petaling Jaya','Specialises in tech recruitment solutions','Active'),
(4,'CodeWave Sdn Bhd','Software','Cyberjaya','Software outsourcing & web application development','Active'),
(4,'CodeWave Labs','Consulting','Kuala Lumpur','R&D and enterprise solutions','Active'),
(5,'TechForge Solutions','IT Services','Penang','Full-stack development and digital transformation','Active'),
(5,'TechForge Cloud','Cloud Services','George Town','Cloud migration & managed services','Active'),
(6,'PixelHub Studio','Design & UX','Kuala Lumpur','Product design and UX for startups','Active'),
(6,'PixelHub Interactive','Media','Kuala Lumpur','Interactive media and front-end experiences','Active'),
(7,'NovaLabs','Software','Johor Bahru','AI & data analytics startup','Active'),
(7,'NovaLabs Solutions','Enterprise','Johor Bahru','Enterprise AI integrations','Active'),
(8,'RecruitNow Sdn Bhd','Recruitment','Ipoh','Local recruitment firm focusing on SMEs','Active'),
(8,'RecruitNow Tech','Tech Recruitment','Ipoh','Specialised tech hiring','Active'),
(9,'RocketForge','DevOps','Kota Kinabalu','DevOps consultancy and infrastructure','Active'),
(9,'RocketForge Labs','SRE','Kota Kinabalu','Site reliability and automation services','Active'),
(10,'HRSmart Malaysia','HR Software','Kuala Lumpur','HR SaaS platform for SMEs','Active'),
(10,'HRSmart Insights','Data','Kuala Lumpur','People analytics & dashboards','Active'),
(3,'AceTemp Solutions','Staffing','Penang','Temporary staffing and payroll services','Active'),
(4,'WaveTech Integrations','Integration','Shah Alam','API integrations and middleware services','Active'),
(5,'ForgeEdge','Security','Cyberjaya','Application security and penetration testing','Active'),
(6,'PixelWorks Studio','Product','Petaling Jaya','MVPs & startup product builds','Active');

-- -------------------------------------------------------
-- End of Part 1
-- Next: Part 2 (job_listing, resume, ai_resume_analysis, ai_resume_evaluation)
-- I will generate Part 2 when you're ready (I'll proceed automatically).
-- -------------------------------------------------------




-- -------------------------------------------------------
-- PART 2: JOB LISTINGS (20 listings)
-- recruiter_ids = 3–10
-- company_id  mapped in rotation to keep ownership realistic
-- -------------------------------------------------------
INSERT INTO job_listing (user_id, company_id, job_title, job_description, job_requirements, salary_min, salary_max, job_status)
VALUES
(3, 1, 'Junior HR Coordinator', 'Assist in candidate sourcing and screening.', 'Communication, Coordination, MS Office', 2500, 3500, 'Open'),
(3, 2, 'Technical Recruiter', 'Recruit IT talent for various clients.', 'Recruitment, Sourcer Tools, Interviews', 3500, 5500, 'Open'),
(4, 3, 'Backend .NET Developer', 'Maintain .NET APIs for enterprise apps.', '.NET Core, SQL Server, Git', 4000, 6000, 'Open'),
(4, 4, 'Frontend React Developer', 'Build responsive UIs.', 'React, Tailwind, REST APIs', 3800, 5500, 'Open'),
(5, 5, 'IT Support Specialist', 'Provide first-line support.', 'Windows, Networking, Troubleshooting', 2800, 4200, 'Open'),
(5, 6, 'Cloud System Engineer', 'Manage cloud infra deployments.', 'AWS/Azure, CI/CD, Linux', 5500, 8500, 'Draft'),
(6, 7, 'UI/UX Designer', 'Design web and mobile interfaces.', 'Figma, User Testing, Prototyping', 3500, 5200, 'Open'),
(6, 8, 'Graphic Illustrator', 'Create brand identity assets.', 'Illustrator, Photoshop, Branding', 2500, 4000, 'Open'),
(7, 9, 'AI Data Analyst', 'Analyze datasets for ML models.', 'Python, SQL, Data Visualization', 4200, 6500, 'Open'),
(7,10, 'Machine Learning Engineer', 'Develop ML pipelines.', 'TensorFlow, PyTorch, AWS', 6000, 9500, 'Draft'),
(8,11, 'Recruitment Consultant', 'Manage recruitment pipelines.', 'Sourcing, Candidate Management', 3000, 5000, 'Open'),
(8,12, 'Talent Acquisition Lead', 'Lead hiring strategy.', 'Leadership, Recruitment Strategy', 6000, 9000, 'Open'),
(9,13, 'DevOps Engineer', 'Automate infra & pipelines.', 'Docker, Kubernetes, Linux', 5500, 9000, 'Open'),
(9,14, 'SRE Engineer', 'Maintain high uptime systems.', 'Monitoring, Kubernetes, Python', 6500, 10500, 'Draft'),
(10,15, 'HR System Analyst', 'Support clients using HR SaaS.', 'SQL, HRIS, Documentation', 3500, 5500, 'Open'),
(10,16, 'HR Data Specialist', 'Analyze HR metrics.', 'Excel, PowerBI, Reporting', 4000, 6500, 'Open'),
(3,17, 'Contract Recruiter', 'Short-term recruitment sourcing.', 'Sourcing Tools, Interviewing', 3000, 4800, 'Open'),
(4,18, 'API Integration Developer', 'Develop integration middleware.', 'Node.js, REST, SQL', 4500, 7000, 'Open'),
(5,19, 'Cybersecurity Analyst', 'Monitor and secure infra.', 'SIEM, SOC, Linux Security', 5500, 8000, 'Open'),
(6,20, 'Product UI Developer', 'Build UI for SaaS products.', 'Vue/React, HTML/CSS, UX basics', 3800, 5500, 'Open');

-- -------------------------------------------------------
-- PART 2: RESUMES (20 resumes, each JobSeeker gets 2)
-- JobSeekers: IDs 11–20
-- -------------------------------------------------------
INSERT INTO resume (user_id, file_path) VALUES
(11, '/uploads/hafiz_resume_v1.pdf'),
(11, '/uploads/hafiz_resume_v2.pdf'),
(12, '/uploads/aisyah_resume_v1.pdf'),
(12, '/uploads/aisyah_resume_v2.pdf'),
(13, '/uploads/amira_resume_v1.pdf'),
(13, '/uploads/amira_resume_v2.pdf'),
(14, '/uploads/jason_resume_v1.pdf'),
(14, '/uploads/jason_resume_v2.pdf'),
(15, '/uploads/syafiqah_resume_v1.pdf'),
(15, '/uploads/syafiqah_resume_v2.pdf'),
(16, '/uploads/irfan_resume_v1.pdf'),
(16, '/uploads/irfan_resume_v2.pdf'),
(17, '/uploads/suresh_resume_v1.pdf'),
(17, '/uploads/suresh_resume_v2.pdf'),
(18, '/uploads/meiling_resume_v1.pdf'),
(18, '/uploads/meiling_resume_v2.pdf'),
(19, '/uploads/zulkifli_resume_v1.pdf'),
(19, '/uploads/zulkifli_resume_v2.pdf'),
(20, '/uploads/farhana_resume_v1.pdf'),
(20, '/uploads/farhana_resume_v2.pdf');

-- -------------------------------------------------------
-- PART 2: AI RESUME ANALYSIS (20)
-- 1:1 to resume_id
-- -------------------------------------------------------
INSERT INTO ai_resume_analysis (resume_id, grammar_score, formatting_score, completeness_score, suggestions)
VALUES
(1,78,72,75,'Add clearer job role descriptions.'),
(2,82,77,80,'Improve layout consistency.'),
(3,85,81,82,'Strong education background. Add projects.'),
(4,88,86,83,'Good formatting; highlight achievements.'),
(5,91,87,89,'Very strong resume.'),
(6,75,70,72,'Expand work experience details.'),
(7,80,77,79,'Consider adding leadership examples.'),
(8,84,79,81,'Good structure, minor tweaks suggested.'),
(9,70,65,68,'Add technical skill keywords.'),
(10,73,68,70,'Improve work history bullet clarity.'),
(11,90,88,86,'Strong resume with clear progression.'),
(12,82,76,78,'Consider adding certification section.'),
(13,77,71,74,'Add project impact statements.'),
(14,79,75,76,'Formatting good; expand achievements.'),
(15,88,82,85,'Solid; highlight portfolio links.'),
(16,74,69,71,'Add more industry keywords.'),
(17,93,90,88,'Excellent technical portfolio.'),
(18,86,82,84,'Very strong project section.'),
(19,72,67,70,'Improve clarity in job role summaries.'),
(20,89,86,87,'Well structured and professional.');

-- -------------------------------------------------------
-- PART 2: AI RESUME EVALUATION (20)
-- resume_id i → job_listing_id i
-- -------------------------------------------------------
INSERT INTO ai_resume_evaluation (job_listing_id, resume_id, match_score)
VALUES
(1,1,76),(2,2,81),(3,3,84),(4,4,79),(5,5,73),
(6,6,68),(7,7,83),(8,8,71),(9,9,87),(10,10,74),
(11,11,80),(12,12,77),(13,13,85),(14,14,69),(15,15,82),
(16,16,75),(17,17,89),(18,18,78),(19,19,72),(20,20,86);





-- -------------------------------------------------------
-- PART 3: JOB APPLICATIONS (20 records, APP2: all Submitted)
-- JobSeekers = user_id 11..20
-- JobListings = job_listing_id 1..20
-- -------------------------------------------------------
INSERT INTO job_application (user_id, job_listing_id, application_status)
VALUES
(11, 1, 'Submitted'),
(12, 2, 'Submitted'),
(13, 3, 'Submitted'),
(14, 4, 'Submitted'),
(15, 5, 'Submitted'),
(16, 6, 'Submitted'),
(17, 7, 'Submitted'),
(18, 8, 'Submitted'),
(19, 9, 'Submitted'),
(20,10, 'Submitted'),
(11,11, 'Submitted'),
(12,12, 'Submitted'),
(13,13, 'Submitted'),
(14,14, 'Submitted'),
(15,15, 'Submitted'),
(16,16, 'Submitted'),
(17,17, 'Submitted'),
(18,18, 'Submitted'),
(19,19, 'Submitted'),
(20,20, 'Submitted');

-- At this point application_id = 1..20 in order.

-- -------------------------------------------------------
-- PART 3: JOB SEEKER NOTES (20 notes, linked to correct recruiters)
-- recruiter_id pulled from job_listing.user_id for each job
-- -------------------------------------------------------
INSERT INTO job_seeker_note (job_seeker_id, job_recruiter_id, application_id, note_text)
VALUES
(11, 3,  1, 'Initial screening note.'),
(12, 3,  2, 'Resume looks good.'),
(13, 4,  3, 'Candidate has .NET experience.'),
(14, 4,  4, 'Front-end portfolio reviewed.'),
(15, 5,  5, 'Customer service background noted.'),
(16, 5,  6, 'Strong cloud fundamentals.'),
(17, 6,  7, 'Creative portfolio attached.'),
(18, 6,  8, 'Great visual style.'),
(19, 7,  9, 'Data analysis skills acceptable.'),
(20, 7, 10, 'ML coursework included.'),
(11, 8, 11, 'Strong communication skills.'),
(12, 8, 12, 'Leadership experience present.'),
(13, 9, 13, 'Good DevOps exposure.'),
(14, 9, 14, 'SRE interest noted.'),
(15,10, 15, 'HR system understanding strong.'),
(16,10, 16, 'Good familiarity with reporting tools.'),
(17, 3, 17, 'Contract role interest confirmed.'),
(18, 4, 18, 'API integration knowledge adequate.'),
(19, 5, 19, 'Cybersecurity basics covered.'),
(20, 6, 20, 'Experience in UI development.');

-- -------------------------------------------------------
-- PART 3: JOB POST APPROVAL (20 approvals, reviewed by Admins 1 & 2)
-- -------------------------------------------------------
INSERT INTO job_post_approval (user_id, job_listing_id, approval_status, comments)
VALUES
(1, 1, 'Pending','Awaiting HR review'),
(2, 2, 'Pending','Pending internal check'),
(1, 3, 'Pending','Awaiting approval'),
(2, 4, 'Pending','Pending review'),
(1, 5, 'Pending','Awaiting details'),
(2, 6, 'Pending','Pending screening'),
(1, 7, 'Pending','Awaiting review'),
(2, 8, 'Pending','Pending approval'),
(1, 9, 'Pending','Awaiting checks'),
(2,10, 'Pending','Requires admin review'),
(1,11, 'Pending','Pending authorization'),
(2,12, 'Pending','Awaiting assignment'),
(1,13, 'Pending','Pending discussion'),
(2,14, 'Pending','Awaiting approval flow'),
(1,15, 'Pending','Requires decision'),
(2,16, 'Pending','Pending validation'),
(1,17, 'Pending','Awaiting final review'),
(2,18, 'Pending','Pending verification'),
(1,19, 'Pending','Awaiting HR approval'),
(2,20, 'Pending','Awaiting review cycle');






-- -------------------------------------------------------
-- PART 4: CONVERSATIONS (20)
-- conversation_id will match job_listing_id for simplicity
-- -------------------------------------------------------
INSERT INTO conversation (job_listing_id) VALUES
(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),
(11),(12),(13),(14),(15),(16),(17),(18),(19),(20);

-- -------------------------------------------------------
-- PART 4: MESSAGES (Professional Tone)
-- recruiter → jobseeker, then jobseeker → recruiter
-- -------------------------------------------------------
INSERT INTO message (conversation_id, sender_id, receiver_id, msg_content) VALUES
-- Job Listing 1..20 — based on recruiter & applicant mapping
(1, 3, 11, 'Thank you for applying. Are you available for a short call this week?'),
(1,11,  3, 'Yes, I am available. Please advise a suitable time.'),

(2, 3, 12, 'Thank you for your application. May we schedule an intro discussion?'),
(2,12,  3, 'Sure, I can make time this week.'),

(3, 4, 13, 'We have received your application. Are you free for a brief call tomorrow?'),
(3,13,  4, 'Yes, tomorrow works for me.'),

(4, 4, 14, 'Thank you for applying. Could we arrange a preliminary interview?'),
(4,14,  4, 'Yes, I am available. Please share the details.'),

(5, 5, 15, 'We appreciate your application. Are you open for a short discussion this week?'),
(5,15,  5, 'Yes, I am open. Kindly propose timing.'),

(6, 5, 16, 'Thank you for your interest. May we schedule an online interview session?'),
(6,16,  5, 'Yes, an online session is fine for me.'),

(7, 6, 17, 'Thank you for applying. Are you available for an interview this week?'),
(7,17,  6, 'Yes, I can make time. Please let me know the schedule.'),

(8, 6, 18, 'We have reviewed your application. When are you free for a discussion?'),
(8,18,  6, 'I am available most afternoons this week.'),

(9, 7, 19, 'Thank you for applying. May we arrange a short intro call?'),
(9,19,  7, 'Yes, that works. Please share a proposed time.'),

(10,7, 20, 'Thank you for your application. Are you available for a call soon?'),
(10,20,7, 'Yes, I am available. Please advise date and time.'),

(11,8, 11, 'We have received your application. Can we schedule a quick discussion?'),
(11,11,8,'Yes, I can make time. Let me know your availability.'),

(12,8, 12, 'Thank you for applying. Are you open for a call this week?'),
(12,12,8,'Yes, I am available this week.'),

(13,9, 13, 'Thank you for your interest. When can we arrange a short call?'),
(13,13,9,'I am available most mornings.'),

(14,9, 14, 'We would like to discuss your application. Are you available soon?'),
(14,14,9,'Yes, please propose a time.'),

(15,10,15, 'Thank you for applying. May we set an interview schedule?'),
(15,15,10,'Yes, I am available. Please advise timing.'),

(16,10,16, 'We have reviewed your profile. Can we arrange a call?'),
(16,16,10,'Yes, that works. Let me know the schedule.'),

(17,3, 17, 'Thank you for applying. Are you available for a call this week?'),
(17,17,3,'Yes, I am available this week.'),

(18,4, 18, 'We appreciate your application. May we schedule an interview?'),
(18,18,4,'Yes, please share the details.'),

(19,5, 19, 'Thank you for your interest. When are you free for a call?'),
(19,19,5,'I am free most weekdays.'),

(20,6, 20, 'We would like to discuss your application soon. Are you available?'),
(20,20,6,'Yes, I am available. Please advise the schedule.');

-- -------------------------------------------------------
-- PART 4: CONVERSATION MONITOR (Admins alternate)
-- -------------------------------------------------------
INSERT INTO conversation_monitor (conversation_id, user_id, flag, date_reviewed) VALUES
(1,1,0,NULL),(2,2,0,NULL),(3,1,0,NULL),(4,2,0,NULL),(5,1,0,NULL),
(6,2,0,NULL),(7,1,0,NULL),(8,2,0,NULL),(9,1,0,NULL),(10,2,0,NULL),
(11,1,0,NULL),(12,2,0,NULL),(13,1,0,NULL),(14,2,0,NULL),(15,1,0,NULL),
(16,2,0,NULL),(17,1,0,NULL),(18,2,0,NULL),(19,1,0,NULL),(20,2,0,NULL);





-- -------------------------------------------------------
-- PART 5: NOTIFICATION PREFERENCES (1 per user, 20 total)
-- -------------------------------------------------------
INSERT INTO notification_preference (user_id, allow_email, allow_inApp, allow_SMS) VALUES
(1,1,1,0),(2,1,1,0),(3,1,1,0),(4,1,1,0),(5,1,1,0),
(6,1,1,0),(7,1,1,0),(8,1,1,0),(9,1,1,0),(10,1,1,0),
(11,1,1,0),(12,1,1,0),(13,1,1,0),(14,1,1,0),(15,1,1,0),
(16,1,1,0),(17,1,1,0),(18,1,1,0),(19,1,1,0),(20,1,1,0);

-- -------------------------------------------------------
-- PART 5: NOTIFICATIONS (20)
-- Sent to jobseekers about recruiter messages or updates
-- -------------------------------------------------------
INSERT INTO notification (user_id, notification_title, notification_msg, notification_type)
VALUES
(11,'Application Update','Your application has been received.','Application'),
(12,'Application Update','Your application has been received.','Application'),
(13,'Application Update','Your application has been received.','Application'),
(14,'Application Update','Your application has been received.','Application'),
(15,'Application Update','Your application has been received.','Application'),
(16,'Screening Scheduled','A recruiter has requested a call.','Interview'),
(17,'Screening Scheduled','A recruiter has requested a call.','Interview'),
(18,'Screening Scheduled','A recruiter has requested a call.','Interview'),
(19,'Screening Scheduled','A recruiter has requested a call.','Interview'),
(20,'Screening Scheduled','A recruiter has requested a call.','Interview'),
(3,'New Application','A new applicant has applied to your job posting.','Employer'),
(4,'New Application','A new applicant has applied to your job posting.','Employer'),
(5,'New Application','A new applicant has applied to your job posting.','Employer'),
(6,'New Application','A new applicant has applied to your job posting.','Employer'),
(7,'New Application','A new applicant has applied to your job posting.','Employer'),
(8,'Profile Viewed','A job seeker viewed your company profile.','Employer'),
(9,'Profile Viewed','A job seeker viewed your company profile.','Employer'),
(10,'Profile Viewed','A job seeker viewed your company profile.','Employer'),
(1,'System Action','Automatic review logs have been updated.','System'),
(2,'System Action','Automatic review logs have been updated.','System');

-- -------------------------------------------------------
-- PART 5: ADMIN LOG (20)
-- -------------------------------------------------------
INSERT INTO admin_log (user_id, action_type) VALUES
(1,'System Initialization'),
(2,'Reviewed job posting approvals'),
(1,'Monitored recruiter activity'),
(2,'Updated notification settings'),
(1,'Viewed flagged conversations'),
(2,'Checked AI analysis summaries'),
(1,'Exported user records'),
(2,'Modified resume review rules'),
(1,'Invited recruiter to compliance review'),
(2,'Cleared stale logs'),
(1,'Ran daily audit tasks'),
(2,'Reviewed message reports'),
(1,'Checked application pipeline flow'),
(2,'Processed system cleanup'),
(1,'Enabled system monitoring trigger'),
(2,'Analyzed message sentiment data'),
(1,'Checked notification queue'),
(2,'Performed backup validation'),
(1,'Reviewed data integrity checks'),
(2,'Completed scheduled maintenance');

-- -------------------------------------------------------
-- PART 5: TEMPLATES (20 total, TEMP2 friendly tone)
-- user_id chosen from recruiter IDs 3–10
-- -------------------------------------------------------
INSERT INTO template (user_id, template_name, template_subject, template_body) VALUES
(3,'Interview Invite','Interview for {{JobTitle}}',
'Hi {{FirstName}},\n\nThanks for applying for {{JobTitle}} at {{Company}}. We''d like to speak with you briefly. Are you available on {{Date}} at {{Time}}?\n\nBest regards,\n{{RecruiterName}}'),

(3,'Rejection - Not a Fit','Application for {{JobTitle}}',
'Hi {{FirstName}},\n\nThank you for your interest in {{JobTitle}}. After review, we will not be moving forward at this time.\n\nWe appreciate the effort you put into applying.\n{{Company}} Recruitment'),

(3,'Next Steps','Next steps for {{JobTitle}}',
'Hi {{FirstName}},\n\nWe''re happy to proceed to the next stage. Please complete:\n- Task: {{TaskName}}\n- Due: {{DueDate}}\n\nThank you,\n{{RecruiterName}}'),

(4,'Call Availability Request','Checking Availability for {{JobTitle}}',
'Hi {{FirstName}},\n\nHope you''re doing well. When would you be available for a short call regarding {{JobTitle}}?\n\nBest,\n{{RecruiterName}}'),

(4,'Portfolio Request','Portfolio Request for {{JobTitle}}',
'Hi {{FirstName}},\n\nCould you please share your portfolio or recent work samples? This will help us evaluate your fit for {{JobTitle}}.\n\nThanks,\n{{RecruiterName}}'),

(5,'Interview Confirmation','Interview Confirmed for {{JobTitle}}',
'Hi {{FirstName}},\n\nYour interview for {{JobTitle}} has been confirmed for {{Date}} at {{Time}}.\nLooking forward to speaking with you.\n\nRegards,\n{{RecruiterName}}'),

(5,'Follow-Up Reminder','Follow-Up for {{JobTitle}}',
'Hi {{FirstName}},\n\nJust checking in to see if you''re still interested in {{JobTitle}}.\n\nLet me know anytime,\n{{RecruiterName}}'),

(6,'Technical Assessment','Assessment for {{JobTitle}}',
'Hi {{FirstName}},\n\nThanks for your interest in {{JobTitle}}. Please complete the technical assessment using the link below:\n{{AssessmentLink}}\n\nGood luck!\n{{RecruiterName}}'),

(6,'Soft Skills Interview','Next Step for {{JobTitle}}',
'Hi {{FirstName}},\n\nWe''d like to schedule a soft skills interview.\nPlease share your availability.\n\nThanks,\n{{RecruiterName}}'),

(7,'Offer Discussion','Discussing Your Offer',
'Hi {{FirstName}},\n\nWe''d like to discuss the potential offer for {{JobTitle}}.\nAre you free for a call sometime this week?\n\nRegards,\n{{RecruiterName}}'),

(7,'Document Request','Documents Required for {{JobTitle}}',
'Hi {{FirstName}},\n\nCould you provide copies of:\n- IC / Passport\n- Latest Payslip (if applicable)\n\nThank you,\n{{RecruiterName}}'),

(8,'Interview Reschedule','Reschedule Interview for {{JobTitle}}',
'Hi {{FirstName}},\n\nWe may need to adjust the interview time.\nLet me know your flexibility.\n\nThanks,\n{{RecruiterName}}'),

(8,'Job Application Update','Update on Your Application for {{JobTitle}}',
'Hi {{FirstName}},\n\nYour application is still under review. We will update you soon.\n\nWarm regards,\n{{RecruiterName}}'),

(9,'Final Interview Invite','Final Round for {{JobTitle}}',
'Hi {{FirstName}},\n\nCongratulations — we’d like to invite you to a final discussion for {{JobTitle}}.\nPlease confirm your availability.\n\nBest,\n{{RecruiterName}}'),

(9,'Reference Check','Reference Check for {{JobTitle}}',
'Hi {{FirstName}},\n\nWe are now conducting reference checks.\nPlease provide contact details of 1–2 past supervisors.\n\nThanks,\n{{RecruiterName}}'),

(10,'Welcome Message','Welcome to {{Company}}!',
'Hi {{FirstName}},\n\nWelcome aboard! We’re excited to have you as part of {{Company}}.\nWe will send onboarding steps shortly.\n\nWarm regards,\n{{RecruiterName}}'),

(10,'Onboarding Instructions','Onboarding for {{JobTitle}}',
'Hi {{FirstName}},\n\nHere are your onboarding steps:\n1. Complete HR forms\n2. Review company handbook\n3. Confirm equipment needs\n\nWelcome!\n{{RecruiterName}}'),

(3,'Interview Reminder','Reminder: Interview for {{JobTitle}}',
'Hi {{FirstName}},\n\nThis is a friendly reminder for your interview scheduled on {{Date}} at {{Time}}.\n\nSee you soon,\n{{RecruiterName}}'),

(4,'Availability Check','Checking Schedule for {{JobTitle}}',
'Hi {{FirstName}},\n\nCould you share your availability for a follow-up conversation?\n\nRegards,\n{{RecruiterName}}'),

(6,'General Follow-Up','Following Up on {{JobTitle}}',
'Hi {{FirstName}},\n\nJust touching base — let me know if you have any updates on your end.\n\nBest wishes,\n{{RecruiterName}}');


