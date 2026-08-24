export interface Quote {
  id: number;
  author: string;
  text: string;
  createdByUserId: string | null;
}
