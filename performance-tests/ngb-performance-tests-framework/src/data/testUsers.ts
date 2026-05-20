export interface NgbTestUser {
  readonly username: string;
  readonly persona: string;
}

export function defaultTestUser(username: string): NgbTestUser {
  return {
    username,
    persona: 'default',
  };
}
