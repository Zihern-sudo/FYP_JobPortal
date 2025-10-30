ALTER TABLE `user`
MODIFY `user_status` ENUM('Active', 'Suspended', 'Inactive') NOT NULL DEFAULT 'Active';

/* allow for inactive */